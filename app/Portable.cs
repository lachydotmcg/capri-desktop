using System;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class Portable
    {
        internal const string Marker = "portable.mode";
        internal static Portable Current;
        internal static volatile bool Paused;
        internal static string Problem;
        internal static Func<string, bool> Removable = IsRemovable;

        internal readonly string Root;
        internal readonly string Id;
        internal readonly string Data;
        internal readonly string Memory;
        internal readonly string Conversations;
        internal readonly string Settings;
        internal readonly string Knowledge;
        internal readonly string Ai;
        internal readonly string Runtime;
        internal readonly string Models;
        internal readonly string Vision;
        internal readonly string Embeddings;
        internal readonly string Voices;
        internal readonly string Plugins;
        internal readonly string Downloads;
        internal readonly string Logs;

        private Portable(string root, string id)
        {
            Root = root;
            Id = id;
            Data = Path.Combine(root, "Data");
            Memory = Path.Combine(Data, "Memory");
            Conversations = Path.Combine(Data, "Conversations");
            Settings = Path.Combine(Data, "Settings");
            Knowledge = Path.Combine(Data, "Knowledge");
            Ai = Path.Combine(root, "AI");
            Runtime = Path.Combine(Ai, "Runtime");
            Models = Path.Combine(Ai, "Models");
            Vision = Path.Combine(Ai, "Vision");
            Embeddings = Path.Combine(Ai, "Embeddings");
            Voices = Path.Combine(root, "Voice");
            Plugins = Path.Combine(root, "Plugins");
            Downloads = Path.Combine(root, "Downloads");
            Logs = Path.Combine(root, "Logs");
        }

        internal string[] Folders
        {
            get { return new[] { Memory, Conversations, Settings, Knowledge, Runtime, Models, Vision, Embeddings, Voices, Plugins, Downloads, Logs }; }
        }

        internal static Portable Detect(string executable)
        {
            Problem = null;
            string root;
            try { root = Path.GetDirectoryName(Path.GetFullPath(executable)); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
            if (string.IsNullOrEmpty(root)) return null;
            bool marked = File.Exists(Path.Combine(root, Marker));
            if (!marked && !Removable(root)) return null;
            try
            {
                string id = ReadId(root);
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    File.WriteAllText(Path.Combine(root, Marker), id + Environment.NewLine);
                }
                Portable found = new Portable(root, id);
                foreach (string folder in found.Folders) Directory.CreateDirectory(folder);
                return found;
            }
            catch (IOException error) { Problem = "She couldn't set up her USB folders in " + root + ": " + error.Message; }
            catch (UnauthorizedAccessException error) { Problem = "She couldn't set up her USB folders in " + root + ": " + error.Message; }
            return null;
        }

        internal static string ReadId(string root)
        {
            try { return File.ReadAllText(Path.Combine(root, Marker)).Trim(); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal bool Here()
        {
            string id = ReadId(Root);
            return id != null && id == Id;
        }

        internal static bool IsRemovable(string path)
        {
            try
            {
                string drive = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(drive) || drive.StartsWith("\\\\", StringComparison.Ordinal)) return false;
                return new DriveInfo(drive).DriveType == DriveType.Removable;
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static void Write(string path, string text)
        {
            if (Paused) throw new IOException("Her USB drive is out.");
            if (Current == null) { File.WriteAllText(path, text); return; }
            string temporary = path + ".new";
            File.WriteAllText(temporary, text);
            Swap(temporary, path);
        }

        internal static void Write(string path, byte[] bytes)
        {
            if (Paused) throw new IOException("Her USB drive is out.");
            if (Current == null) { File.WriteAllBytes(path, bytes); return; }
            string temporary = path + ".new";
            File.WriteAllBytes(temporary, bytes);
            Swap(temporary, path);
        }

        private static void Swap(string temporary, string path)
        {
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }

    internal sealed class DriveGuard : IDisposable
    {
        private readonly Func<bool> present;
        private readonly Timer timer;
        private readonly int interval;
        private readonly object gate = new object();
        private int polling;
        private bool away;

        internal event Action Gone;
        internal event Action Back;

        internal DriveGuard(Func<bool> present, int interval)
        {
            this.present = present;
            this.interval = interval;
            timer = new Timer(delegate { Poll(); }, null, Timeout.Infinite, Timeout.Infinite);
        }

        internal bool Away { get { lock (gate) return away; } }

        internal void Start() { timer.Change(interval, interval); }

        internal void Poll()
        {
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                bool here;
                try { here = present(); }
                catch (IOException) { here = false; }
                catch (UnauthorizedAccessException) { here = false; }
                Action raise = null;
                lock (gate)
                {
                    if (here && away) { away = false; Portable.Paused = false; raise = Back; }
                    else if (!here && !away) { away = true; Portable.Paused = true; raise = Gone; }
                }
                if (raise != null) raise();
            }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        public void Dispose() { timer.Dispose(); }
    }
}

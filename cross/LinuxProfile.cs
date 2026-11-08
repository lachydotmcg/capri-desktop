using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    internal sealed class LinuxProfile : IDisposable
    {
        [DllImport("libc")] private static extern uint umask(uint mode);
        private readonly FileStream writer;
        internal LinuxProfile()
        {
            umask(0x3f);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string path = Path.Combine(folder, "native-writer.lock");
            if (new FileInfo(path).LinkTarget != null) throw new IOException("The native profile lock cannot be a link.");
            writer = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            try { writer.Lock(0, 1); }
            catch { writer.Dispose(); throw new IOException("This native Capri profile already has a writer. Open a separate profile or use its running window."); }
        }
        public void Dispose() { writer.Dispose(); }
    }
}

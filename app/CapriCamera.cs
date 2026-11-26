using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace CapriDesktop
{
#if !CROSS
    internal sealed class CapriCamera : IDisposable
    {
        internal const string ClassId = "{6F1C2A7E-4D0B-4B8E-9A51-C4A7E2B13D90}";
        private const string Resource64 = "Capri.Camera64.dll";
        private const string Resource32 = "Capri.Camera32.dll";
        private enum Source { Room, Portrait, ClearingPortrait }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateCamera(int width, int height, float framerate);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DeleteCamera(IntPtr camera);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SendFrame(IntPtr camera, byte[] image);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);

        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private byte[] waiting;
        private long lastFrame, offAt;
        private bool closed, off, roomArmed = true;
        private Source source;
        private int portraitTurn;
        private Thread worker;

        internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Capri Camera"); } }

        internal static bool Bundled
        {
            get
            {
                Assembly own = Assembly.GetExecutingAssembly();
                return own.GetManifestResourceInfo(Resource64) != null && own.GetManifestResourceInfo(Resource32) != null;
            }
        }

        internal static bool Registered
        {
            get
            {
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                    using (RegistryKey server = root.OpenSubKey(@"CLSID\" + ClassId + @"\InprocServer32"))
                    {
                        string path = server == null ? null : server.GetValue(null) as string;
                        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                    }
                }
                return true;
            }
        }

        internal static string State { get { return !Bundled ? "missing" : Registered && File.Exists(Path.Combine(Folder, "CapriCamera64.dll")) ? "ready" : "needs-install"; } }

        internal static string AskInstall()
        {
            if (State != "needs-install") return State;
            try
            {
                using (Process run = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--camera-install") { UseShellExecute = true, Verb = "runas" }))
                {
                    if (run == null || !run.WaitForExit(120000)) return State;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return "needs-install"; }
            return State;
        }

        internal static int Install()
        {
            try
            {
                string folder = Folder;
                Directory.CreateDirectory(folder);
                string dll64 = Path.Combine(folder, "CapriCamera64.dll"), dll32 = Path.Combine(folder, "CapriCamera32.dll");
                Extract(Resource64, dll64);
                Extract(Resource32, dll32);
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!Environment.Is64BitOperatingSystem) return Register(Path.Combine(windows, "System32", "regsvr32.exe"), dll32) == 0 ? 0 : 3;
                string system = Path.Combine(windows, Environment.Is64BitProcess ? "System32" : "Sysnative");
                if (Register(Path.Combine(system, "regsvr32.exe"), dll64) != 0) return 2;
                if (Register(Path.Combine(windows, "SysWOW64", "regsvr32.exe"), dll32) != 0) return 3;
                return 0;
            }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
        }

        internal static int Remove()
        {
            string folder = Folder;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string dll64 = Path.Combine(folder, "CapriCamera64.dll"), dll32 = Path.Combine(folder, "CapriCamera32.dll");
            if (File.Exists(dll64)) Register(Path.Combine(windows, Environment.Is64BitProcess ? "System32" : "Sysnative", "regsvr32.exe"), dll64, true);
            if (File.Exists(dll32) && Environment.Is64BitOperatingSystem) Register(Path.Combine(windows, "SysWOW64", "regsvr32.exe"), dll32, true);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
            return 0;
        }

        private static void Extract(string resource, string path)
        {
            using (Stream from = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (from == null) throw new IOException("Capri Camera isn't in this build.");
                using (FileStream to = File.Create(path)) from.CopyTo(to);
            }
        }

        private static int Register(string regsvr, string dll, bool remove = false)
        {
            using (Process run = Process.Start(new ProcessStartInfo(regsvr, (remove ? "/u " : "") + "/s \"" + dll + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (run == null || !run.WaitForExit(30000)) return -1;
                return run.ExitCode;
            }
        }

        internal void Send(byte[] jpeg)
        {
            lock (gate)
            {
                if (closed || source != Source.Room || !roomArmed || off && unchecked(Environment.TickCount - offAt) < 1000) return;
                Queue(jpeg);
            }
            wake.Set();
        }

        internal bool PortraitBusy { get { lock (gate) return source != Source.Room; } }

        internal string RoomState()
        {
            string state = State;
            lock (gate)
            {
                if (source != Source.Room) return "busy";
                if (state == "ready") roomArmed = true;
                return state;
            }
        }

        internal int BeginPortrait()
        {
            lock (gate)
            {
                if (closed || source != Source.Room || worker != null) return 0;
                source = Source.Portrait;
                // Require a new Room camera selection after an explicit portrait session.
                roomArmed = false;
                if (++portraitTurn == 0) portraitTurn++;
                return portraitTurn;
            }
        }

        internal bool PortraitCurrent(int turn)
        {
            lock (gate) return !closed && source == Source.Portrait && turn == portraitTurn;
        }

        internal void SendPortrait(int turn, byte[] jpeg)
        {
            lock (gate)
            {
                if (closed || source != Source.Portrait || turn != portraitTurn) return;
                Queue(jpeg);
            }
            wake.Set();
        }

        internal void EndPortrait(int turn)
        {
            lock (gate)
            {
                if (source != Source.Portrait || turn != portraitTurn) return;
                source = worker == null ? Source.Room : Source.ClearingPortrait;
                MarkOff();
            }
            wake.Set();
        }

        internal void Off()
        {
            lock (gate)
            {
                if (source != Source.Room) return;
                MarkOff();
            }
            wake.Set();
        }

        private void MarkOff()
        {
            waiting = null;
            off = true;
            offAt = Environment.TickCount;
        }

        // Called with gate held. Keep a single worker and only its most recent frame.
        private void Queue(byte[] jpeg)
        {
            waiting = jpeg;
            off = false;
            lastFrame = Environment.TickCount;
            if (worker != null) return;
            bool portrait = source == Source.Portrait;
            worker = new Thread(() => Run(portrait)) { IsBackground = true, Name = "Capri Camera" };
            worker.Start();
        }

        private void Run(bool portrait)
        {
            IntPtr module = IntPtr.Zero;
            bool opened = false;
            try
            {
                module = LoadLibraryW(Path.Combine(Folder, Environment.Is64BitProcess ? "CapriCamera64.dll" : "CapriCamera32.dll"));
                if (module == IntPtr.Zero) return;
                IntPtr create = GetProcAddress(module, "scCreateCamera"), delete = GetProcAddress(module, "scDeleteCamera"), send = GetProcAddress(module, "scSendFrame");
                if (create == IntPtr.Zero || delete == IntPtr.Zero || send == IntPtr.Zero) return;
                opened = true;
                Frames(portrait, (CreateCamera)Marshal.GetDelegateForFunctionPointer(create, typeof(CreateCamera)),
                    (DeleteCamera)Marshal.GetDelegateForFunctionPointer(delete, typeof(DeleteCamera)),
                    (SendFrame)Marshal.GetDelegateForFunctionPointer(send, typeof(SendFrame)));
            }
            finally
            {
                if (module != IntPtr.Zero) FreeLibrary(module);
                lock (gate)
                {
                    worker = null;
                    if (portrait)
                    {
                        source = Source.Room;
                        MarkOff();
                    }
                    else if (opened && !closed && !off && waiting != null) Queue(waiting);
                }
                wake.Set();
            }
        }

        private void Frames(bool portrait, CreateCamera makeCamera, DeleteCamera dropCamera, SendFrame sendFrame)
        {
            IntPtr camera = IntPtr.Zero;
            int width = 0, height = 0;
            try
            {
                while (true)
                {
                    wake.WaitOne(1000);
                    byte[] jpeg;
                    lock (gate)
                    {
                        if (closed) return;
                        jpeg = waiting;
                        waiting = null;
                        if (jpeg == null && (off || unchecked(Environment.TickCount - lastFrame) > 15000)) return;
                    }
                    if (jpeg == null) continue;
                    int w, h;
                    byte[] bgr = Decode(jpeg, out w, out h);
                    if (bgr == null) continue;
                    lock (gate) if (closed || off) return;
                    if (camera == IntPtr.Zero || w != width || h != height)
                    {
                        if (camera != IntPtr.Zero) dropCamera(camera);
                        camera = makeCamera(w, h, 30f);
                        width = w;
                        height = h;
                        if (camera == IntPtr.Zero && portrait) return;
                        if (camera == IntPtr.Zero) { Thread.Sleep(1000); continue; }
                    }
                    lock (gate)
                    {
                        if (closed || off) return;
                        sendFrame(camera, bgr);
                    }
                }
            }
            finally
            {
                if (camera != IntPtr.Zero)
                {
                    // The existing device keeps a darkened copy of the last input. Leave a neutral frame when
                    // closing the portrait source, so a still-connected viewer loses the actual portrait.
                    try { if (portrait) sendFrame(camera, new byte[width * height * 3]); }
                    finally { dropCamera(camera); }
                }
            }
        }

        internal static byte[] Decode(byte[] jpeg, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using (MemoryStream source = new MemoryStream(jpeg))
                using (Bitmap picture = new Bitmap(source))
                {
                    int w = picture.Width / 4 * 4, h = picture.Height / 4 * 4;
                    if (w < 4 || h < 4) return null;
                    BitmapData data = picture.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        byte[] bgr = new byte[w * h * 3];
                        for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bgr, y * w * 3, w * 3);
                        width = w;
                        height = h;
                        return bgr;
                    }
                    finally { picture.UnlockBits(data); }
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
        }

        public void Dispose()
        {
            Thread portrait;
            lock (gate)
            {
                closed = true;
                portrait = source == Source.Room ? null : worker;
            }
            wake.Set();
            // Leave time for the neutral frame to reach an attached viewer on ordinary app exit.
            if (portrait != null && portrait != Thread.CurrentThread) portrait.Join(1000);
        }
    }
#endif
}

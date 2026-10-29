using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CapriDesktop
{
    internal sealed class Pick
    {
        internal readonly string Name;
        internal readonly string Ollama;
        internal readonly string File;
        internal readonly string Url;
        internal readonly string Sha256;
        internal readonly long Size;
        internal string EyesFile;
        internal string EyesUrl;
        internal string EyesSha256;
        internal long EyesSize;

        internal Pick(string name, string ollama, string repository, string revision, string file, string sha256, long size)
        {
            Name = name;
            Ollama = ollama;
            File = file;
            Url = "https://huggingface.co/" + repository + "/resolve/" + revision + "/" + file;
            Sha256 = sha256;
            Size = size;
        }

        internal string Download { get { return Engine.Amount(Size); } }

        internal Pick Eyes(string repository, string revision, string file, string sha256, long size)
        {
            EyesFile = file;
            EyesUrl = "https://huggingface.co/" + repository + "/resolve/" + revision + "/" + file;
            EyesSha256 = sha256;
            EyesSize = size;
            return this;
        }
    }

    internal sealed class Machine
    {
        internal long Ram;
        internal long Vram;
        internal string Gpu;

        [StructLayout(LayoutKind.Sequential)]
        private sealed class MemoryStatus
        {
            internal uint Length = 64;
            internal uint Load;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtended;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        internal static Machine Read()
        {
            Machine machine = new Machine();
            MemoryStatus status = new MemoryStatus();
            if (GlobalMemoryStatusEx(status)) machine.Ram = (long)status.TotalPhysical;
            try
            {
                using (RegistryKey adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (adapters != null)
                        foreach (string name in adapters.GetSubKeyNames())
                        {
                            if (name.Length != 4) continue;
                            try
                            {
                                using (RegistryKey adapter = adapters.OpenSubKey(name))
                                {
                                    if (adapter == null) continue;
                                    long memory = Bytes(adapter.GetValue("HardwareInformation.qwMemorySize"));
                                    if (memory <= 0) memory = Bytes(adapter.GetValue("HardwareInformation.MemorySize"));
                                    if (memory > machine.Vram) { machine.Vram = memory; machine.Gpu = adapter.GetValue("DriverDesc") as string; }
                                }
                            }
                            catch (System.Security.SecurityException) { }
                            catch (UnauthorizedAccessException) { }
                        }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            return machine;
        }

        private static long Bytes(object value)
        {
            if (value is long) return (long)value;
            if (value is int) return (uint)(int)value;
            byte[] raw = value as byte[];
            if (raw != null && raw.Length >= 8) return BitConverter.ToInt64(raw, 0);
            if (raw != null && raw.Length >= 4) return BitConverter.ToUInt32(raw, 0);
            return 0;
        }

        internal string Describe()
        {
            string ram = Math.Round(Ram / (double)(1L << 30)) + " GB RAM";
            if (string.IsNullOrEmpty(Gpu) || Vram < (1L << 30)) return ram;
            return ram + " · " + Gpu.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "Radeon ") + " (" + Math.Round(Vram / (double)(1L << 30)) + " GB)";
        }
    }

    internal sealed class Engine
    {
        internal const string Build = "b11669";
        private const string ZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-vulkan-x64.zip";
        private const string ZipSha = "2c07574e7bde71321cb785282b20cc821e951132149eeceeea32ddebdbf0598e";
        private const long ZipSize = 32182350;
        internal static readonly Pick[] Picks =
        {
            new Pick("Gemma 4 E4B", "gemma4:e4b-it-qat", "ggml-org/gemma-4-E4B-it-GGUF", "a69f018d7678a2b9f5e705f8903d6fb29b9b25dc", "gemma-4-E4B-it-Q4_0.gguf", "a555b900214b477d8880e7832e0b8925e139b0159640036b09fe472b6f2097f2", 4590807392).Eyes("ggml-org/gemma-4-E4B-it-GGUF", "a69f018d7678a2b9f5e705f8903d6fb29b9b25dc", "mmproj-gemma-4-E4B-it-Q8_0.gguf", "197f49a93027f9843772bd24a6a9e0be2a32a788de5a3def330e9c585d86edd1", 559874816),
            new Pick("Gemma 4 E2B", "gemma4:e2b-it-qat", "unsloth/gemma-4-E2B-it-GGUF", "0314792d7f1f7e229411f620751375812bb9faf2", "gemma-4-E2B-it-Q4_K_M.gguf", "740185b21d22ceb83a11c3aa62ad5842ef32c70f6096d756bbee85a1e4ec34b8", 3106738272).Eyes("ggml-org/gemma-4-E2B-it-GGUF", "6938c58e698502037401a5f19f6905f2fb2f7dee", "mmproj-gemma-4-E2B-it-Q8_0.gguf", "9406f99c16d68cda4f1f0552192dcc99021ea1fc6d2fd50b1dc3ccf30d04b292", 557368064),
            new Pick("Qwen3 4B", "qwen3:4b-instruct-2507-q4_K_M", "unsloth/Qwen3-4B-Instruct-2507-GGUF", "a06e946bb6b655725eafa393f4a9745d460374c9", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf", "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597", 2497281120)
        };
        private readonly string root;
        private readonly object gate = new object();
        private Process server;
        private static IntPtr job;
        private static readonly object jobs = new object();
        private int port;
        private bool serverEyes;
        private bool serverCpu;
        private int serverWindow;
        internal volatile bool Cancel;
        internal volatile bool CpuOnly;

        internal Engine(string data)
        {
            root = Path.Combine(data, "brain");
            CpuOnly = File.Exists(CpuMark);
        }

        private string CpuMark { get { return Path.Combine(Folder, "cpu-only.txt"); } }

        internal void KeepCpu()
        {
            try { if (Directory.Exists(Folder)) File.WriteAllText(CpuMark, "The graphics card garbled replies, so the brain runs on the CPU. Delete this file to try the graphics card again.\n"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static Pick Fit(Machine machine)
        {
            const long Gb = 1L << 30;
            if (machine.Vram >= 7 * Gb && machine.Ram >= 12 * Gb) return Picks[0];
            if (machine.Ram >= 7 * Gb || machine.Vram >= 4 * Gb) return Picks[1];
            return Picks[2];
        }

        internal static string Amount(long size) { return size < 1e9 ? Math.Round(size / 1e6) + " MB" : (size / 1e9).ToString("0.#") + " GB"; }

        private string Folder { get { return Path.Combine(root, "engine-" + Build); } }
        private string Server { get { return Path.Combine(Folder, "llama-server.exe"); } }
        private string ModelPath(Pick pick) { return Path.Combine(Path.Combine(root, "models"), pick.File); }
        private string EyesPath(Pick pick) { return pick.EyesFile == null ? null : Path.Combine(Path.Combine(root, "models"), pick.EyesFile); }

        internal bool HasEyes(Pick pick) { string path = EyesPath(pick); return path != null && File.Exists(path); }

        internal void InstallEyes(Pick pick, Action<string, long, long> progress)
        {
            if (pick.EyesFile == null || HasEyes(pick)) return;
            Cancel = false;
            string drive = Path.GetPathRoot(root);
            if (new DriveInfo(drive).AvailableFreeSpace < pick.EyesSize + (64L << 20)) throw new IOException("Storage too full: her eyes need " + Amount(pick.EyesSize) + " free on " + drive.TrimEnd('\\') + ".");
            Fetch(pick.EyesUrl, EyesPath(pick), pick.EyesSize, pick.EyesSha256, "her eyes", progress, delegate { return Cancel; });
        }

        internal Pick Installed()
        {
            if (!File.Exists(Server)) return null;
            foreach (Pick pick in Picks) if (File.Exists(ModelPath(pick))) return pick;
            return null;
        }

        internal string Address { get { lock (gate) return "http://127.0.0.1:" + port; } }

        internal bool Crashed { get { lock (gate) return server != null && server.HasExited; } }

        internal void Install(Pick pick, Action<string, long, long> progress)
        {
            Directory.CreateDirectory(root);
            long needed = (File.Exists(Server) ? 0 : ZipSize * 4) + (File.Exists(ModelPath(pick)) ? 0 : pick.Size) + (256L << 20);
            string drive = Path.GetPathRoot(root);
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("Storage too full: her brain needs " + Amount(needed) + " free on " + drive.TrimEnd('\\') + ".");
            if (!File.Exists(Server))
            {
                string zip = Path.Combine(root, "engine-" + Build + ".zip");
                Fetch(ZipUrl, zip, ZipSize, ZipSha, "the engine", progress, delegate { return Cancel; });
                string staging = Folder + ".new";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                ZipFile.ExtractToDirectory(zip, staging);
                if (!File.Exists(Path.Combine(staging, "llama-server.exe"))) throw new IOException("The engine download is missing llama-server.exe.");
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                Directory.Move(staging, Folder);
                File.Delete(zip);
            }
            if (!File.Exists(ModelPath(pick)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ModelPath(pick)));
                Fetch(pick.Url, ModelPath(pick), pick.Size, pick.Sha256, pick.Name, progress, delegate { return Cancel; });
            }
        }

        internal static void Fetch(string url, string target, long size, string sha256, string label, Action<string, long, long> progress, Func<bool> stopped)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string part = target + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > size) { File.Delete(part); have = 0; }
            int stalls = 0;
            while (have < size)
            {
                long before = have;
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.UserAgent = "Capri-Desktop";
                    request.Timeout = 30000;
                    request.ReadWriteTimeout = 60000;
                    if (have > 0) request.AddRange(have);
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0;
                        using (Stream body = response.GetResponseStream())
                        using (FileStream file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            byte[] buffer = new byte[1 << 16];
                            long shown = 0;
                            int read;
                            while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (stopped()) throw new OperationCanceledException();
                                file.Write(buffer, 0, read);
                                have += read;
                                if (have - shown >= (4L << 20)) { shown = have; progress("Downloading " + label, have, size); }
                            }
                        }
                    }
                }
                catch (WebException) { if (stopped()) throw new OperationCanceledException(); }
                catch (IOException) { if (stopped()) throw new OperationCanceledException(); }
                if (have < size)
                {
                    stalls = have > before ? 0 : stalls + 1;
                    if (stalls >= 4) throw new IOException("The download of " + label + " keeps stopping. Check your connection and try again.");
                    Thread.Sleep(1500 * stalls);
                }
            }
            if (have != size) { File.Delete(part); throw new IOException(label + " came down the wrong size. Try again."); }
            progress("Checking " + label, size, size);
            if (!string.Equals(Hash(part), sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(part); throw new IOException(label + " failed its checksum. Try again."); }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }

        internal static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                StringBuilder text = new StringBuilder();
                foreach (byte value in sha.ComputeHash(file)) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        internal static int Threads(int processors) { return Math.Max(1, Math.Min(4, processors / 2)); }

        internal ProcessStartInfo StartInfo(Pick pick, bool eyes, bool cpu, int number, int window = 4096)
        {
            int threads = Threads(Environment.ProcessorCount);
            ProcessStartInfo info = new ProcessStartInfo(Server, "-m \"" + ModelPath(pick) + "\"" + (eyes ? " --mmproj \"" + EyesPath(pick) + "\"" : "") + " --host 127.0.0.1 --port " + number + " -c " + window + " -np 1 -a capri --jinja --reasoning off --no-webui --sleep-idle-seconds 900 --log-disable -t " + threads + " -tb " + threads + " -b 256 -ub 128 --poll 0 --poll-batch 0" + (cpu ? " --device none -ngl 0 --no-mmproj-offload" : ""));
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WorkingDirectory = Folder;
            return info;
        }

        internal static Func<ProcessStartInfo, Process> Spawn = Process.Start;

        internal static int ExitCode(Process process)
        {
            try { return process.ExitCode; }
            catch (InvalidOperationException) { return -1; }
        }

        internal static void Lower(Process child)
        {
            try { child.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        internal void Ensure(Pick pick)
        {
            lock (gate)
            {
                bool eyes = HasEyes(pick);
                int window = Brain.Window;
                if (server != null && !server.HasExited && serverEyes == eyes && serverCpu == CpuOnly && serverWindow == window) return;
                if (server != null) Stop();
                if (!File.Exists(Server) || !File.Exists(ModelPath(pick))) throw new BrainException("Brain not downloaded. Press Set up her brain.");
                port = FreePort();
                serverEyes = eyes;
                serverCpu = CpuOnly;
                serverWindow = window;
                server = Spawn(StartInfo(pick, eyes, serverCpu, port, window));
                Lower(server);
                Bind(server);
            }
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.Elapsed.TotalSeconds < 180)
            {
                Process running;
                bool onGpu;
                lock (gate) { running = server; onGpu = !serverCpu; }
                if (running == null || running.HasExited)
                {
                    // Its own log is off, so the exit code is the only clue Copy error details can carry. A graphics driver that crashes it while loading gets one try on the CPU, as a crash mid-chat does.
                    bool retry = running != null && onGpu;
                    Brain.Log("brain", -1, "llama-server stopped while loading " + pick.Name + (onGpu ? "" : " on the CPU") + (running == null ? "" : " with exit code 0x" + ((uint)ExitCode(running)).ToString("X8")) + (retry ? ", restarting it on the CPU" : ""));
                    if (!retry) throw new BrainException("Brain crashed loading. Close games, press Refresh.");
                    CpuOnly = true;
                    Ensure(pick);
                    return;
                }
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Address + "/health");
                    request.Proxy = null;
                    request.Timeout = 2000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (WebException) { }
                Thread.Sleep(400);
            }
            throw new BrainException("Brain loaded too slowly. Close games, press Refresh.");
        }

        internal static bool Lost(string problem) { return Regex.IsMatch(problem ?? "", @"ErrorDeviceLost|DEVICE_LOST|device lost|vk::", RegexOptions.IgnoreCase); }

        internal void Stop()
        {
            lock (gate)
            {
                if (server == null) return;
                try { if (!server.HasExited) { server.Kill(); server.WaitForExit(3000); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                server.Dispose();
                server = null;
            }
        }

        internal static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int value = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            internal long ProcessTime;
            internal long JobTime;
            internal uint Flags;
            internal UIntPtr MinimumWorkingSet;
            internal UIntPtr MaximumWorkingSet;
            internal uint ActiveProcesses;
            internal UIntPtr Affinity;
            internal uint Priority;
            internal uint Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            internal BasicLimits Basic;
            internal ulong ReadOperations;
            internal ulong WriteOperations;
            internal ulong OtherOperations;
            internal ulong ReadBytes;
            internal ulong WriteBytes;
            internal ulong OtherBytes;
            internal UIntPtr ProcessMemory;
            internal UIntPtr JobMemory;
            internal UIntPtr PeakProcessMemory;
            internal UIntPtr PeakJobMemory;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, int size);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        internal static void Bind(Process child)
        {
            try
            {
                lock (jobs)
                {
                    if (job == IntPtr.Zero)
                    {
                        IntPtr created = CreateJobObject(IntPtr.Zero, null);
                        ExtendedLimits limits = new ExtendedLimits();
                        limits.Basic.Flags = 0x2000;
                        if (created != IntPtr.Zero && SetInformationJobObject(created, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits)))) job = created;
                    }
                    if (job != IntPtr.Zero) AssignProcessToJobObject(job, child.Handle);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}

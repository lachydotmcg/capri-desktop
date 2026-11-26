using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class EngineAsset
    {
        internal readonly string Name;
        internal readonly string Url;
        internal readonly string Sha256;
        internal readonly long Size;

        internal EngineAsset(string name, string sha256, long size)
        {
            Name = name;
            Url = "https://github.com/ggml-org/llama.cpp/releases/download/" + Engine.Build + "/llama-" + Engine.Build + "-bin-" + name + ".tar.gz";
            Sha256 = sha256;
            Size = size;
        }
    }

    internal sealed partial class Engine
    {
        internal static readonly EngineAsset[] Assets =
        {
            new EngineAsset("macos-arm64", "dbed55493d72bc1ba2babc8770d669c2289b5bc4ee9fb2a4dfc158d45b7d13fd", 12191582),
            new EngineAsset("macos-x64", "cab28238d78158109b48deb27b05d50e0dcef56275d2e76da9ca8281526c7532", 11677404),
            new EngineAsset("ubuntu-x64", "cf07c1c112d709bd67e522017471de57ed0ec8bea031d732c5422351c07f65ac", 17851611),
            new EngineAsset("ubuntu-arm64", "9ac6fda2058d35677608a06f751a50122ddfa4ce0ddb7f084466b8e79b6d4a99", 13900160)
        };

        internal static EngineAsset AssetFor(bool mac, bool linux, Architecture arch)
        {
            string name = null;
            if (mac && arch == Architecture.Arm64) name = "macos-arm64";
            else if (mac && arch == Architecture.X64) name = "macos-x64";
            else if (linux && arch == Architecture.X64) name = "ubuntu-x64";
            else if (linux && arch == Architecture.Arm64) name = "ubuntu-arm64";
            return name == null ? null : Array.Find(Assets, delegate(EngineAsset asset) { return asset.Name == name; });
        }

        internal static EngineAsset ThisAsset()
        {
            return AssetFor(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), RuntimeInformation.IsOSPlatform(OSPlatform.Linux), RuntimeInformation.OSArchitecture);
        }

        internal static Machine ReadMachine()
        {
            Machine machine = new Machine();
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    machine.Ram = long.Parse(Run("sysctl", "-n hw.memsize").Trim());
                    if (RuntimeInformation.OSArchitecture == Architecture.Arm64) { machine.Vram = machine.Ram * 2 / 3; machine.Gpu = "Apple silicon"; }
                }
                else
                {
                    Match total = Regex.Match(File.ReadAllText("/proc/meminfo"), @"MemTotal:\s+(\d+) kB");
                    if (total.Success) machine.Ram = long.Parse(total.Groups[1].Value) * 1024;
                }
            }
            catch (IOException) { }
            catch (FormatException) { }
            catch (System.ComponentModel.Win32Exception) { }
            // A failed read stays zero: her Resources says "-" and Fit picks her smallest model, rather than inventing memory.
            return machine;
        }

        private static string Run(string file, string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments);
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.CreateNoWindow = true;
            using (Process process = Process.Start(info))
            {
                string text = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return text;
            }
        }

        internal static void Unpack(string archive, string target)
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            using (FileStream file = File.OpenRead(archive))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
                System.Formats.Tar.TarFile.ExtractToDirectory(gzip, target, true);
            string[] folders = Directory.GetDirectories(target);
            if (folders.Length == 1 && Directory.GetFiles(target).Length == 0) Flatten(folders[0], target);
            if (OperatingSystem.IsWindows()) return;
            foreach (string path in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith("llama-", StringComparison.Ordinal) || name.StartsWith("lib", StringComparison.Ordinal))
                    File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }

        private static void Flatten(string inner, string target)
        {
            string moved = target + ".inner";
            if (Directory.Exists(moved)) Directory.Delete(moved, true);
            Directory.Move(inner, moved);
            Directory.Delete(target, false);
            Directory.Move(moved, target);
        }

        internal void InstallBinary(Pick pick, Action<string, long, long> progress)
        {
            EngineAsset asset = ThisAsset();
            if (asset == null) throw new IOException("Capri can't run her own brain on this computer yet. Install Ollama from ollama.com.");
            Directory.CreateDirectory(root);
            bool haveEngine = File.Exists(Server);
            bool haveModel = File.Exists(ModelPath(pick));
            long needed = (haveEngine ? 0 : asset.Size * 4) + (haveModel ? 0 : pick.Size) + (256L << 20);
            string drive = Path.GetPathRoot(Path.GetFullPath(root));
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("Storage too full: her brain needs " + Amount(needed) + " free on " + drive.TrimEnd('\\', '/') + ".");
            long all = (haveEngine ? 0 : asset.Size) + (haveModel ? 0 : pick.Size);
            long before = 0;
            Action<string, long, long> overall = delegate(string label, long done, long total) { progress(label, before + done, all); };
            if (!haveEngine)
            {
                string archive = Path.Combine(root, "engine-" + Build + ".tar.gz");
                Fetch(asset.Url, archive, asset.Size, asset.Sha256, "the engine", overall, delegate { return Cancel; });
                string staging = Folder + ".new";
                Unpack(archive, staging);
                if (!File.Exists(Path.Combine(staging, "llama-server"))) throw new IOException("The engine didn't come with llama-server. Try again.");
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                Directory.Move(staging, Folder);
                File.Delete(archive);
                before = asset.Size;
            }
            if (!haveModel)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ModelPath(pick)));
                Fetch(pick.Url, ModelPath(pick), pick.Size, pick.Sha256, pick.Name, overall, delegate { return Cancel; });
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CapriDesktop;
internal static class PackageChecks
{
    private static void VerifyAssembly(string executable)
    {
        using (var stream = File.OpenRead(executable))
        using (var pe = new PEReader(stream))
        {
            var metadata = pe.GetMetadataReader();
            var resources = new Dictionary<string, byte[]>();
            var section = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress);
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                if (!resource.Implementation.IsNil) continue;
                int offset = checked((int)resource.Offset);
                var reader = section.GetReader(offset, 4);
                int length = reader.ReadInt32();
                resources.Add(metadata.GetString(resource.Name), section.GetContent(offset + 4, length).ToArray());
            }
            for (int i = 0; i < AssetCatalog.Sprites.Length; i++)
            {
                string name = "Capri.Sprite." + AssetCatalog.Sprites[i] + ".png";
                if (!resources.ContainsKey(name)) throw new InvalidDataException("Missing embedded " + name);
                using (var data = new MemoryStream(resources[name])) AssetCatalog.ValidatePng(data, AssetCatalog.Frames[i], name);
            }
            foreach (string name in new[] { "Capri.Chip.ico", "Capri.Font.Mono.ttf" })
                if (!resources.ContainsKey(name) || resources[name].Length == 0) throw new InvalidDataException("Missing embedded " + name);
        }
        Console.WriteLine("PASS executable metadata and all 15 embedded sprite/icon/font resources");
    }
    internal static void Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-package") { VerifyAssembly(args[1]); return; }
        if (args.Length != 4 || args[0] != "--package" || !Regex.IsMatch(args[3], "^[0-9a-f]{40}(-dirty)?$"))
            throw new ArgumentException("Usage: --package <build-directory> <artifact-directory> <full-commit[-dirty]>");
        string executable = Path.Combine(args[1], "Capri.exe");
        VerifyAssembly(executable);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        files.Add("Capri.exe", File.ReadAllBytes(executable));
        string configuration = Path.Combine(args[1], "Capri.exe.config");
        if (File.Exists(configuration)) files.Add("Capri.exe.config", File.ReadAllBytes(configuration));
        files.Add("README-FIRST.md", File.ReadAllBytes("docs/README-FIRST.md"));
        files.Add("WINDOWS-SMOKE-TEST.md", File.ReadAllBytes("docs/WINDOWS-SMOKE-TEST.md"));
        files.Add("KNOWN-ISSUES.md", File.ReadAllBytes("docs/KNOWN-ISSUES.md"));
        files.Add("NOTICE.md", File.ReadAllBytes("docs/NOTICE.md"));
        files.Add("OFL-ShareTechMono.txt", File.ReadAllBytes("assets/fonts/OFL-ShareTechMono.txt"));
        files.Add("BUILD-INFO.json", Encoding.UTF8.GetBytes("{\"repository\":\"Shaken89/capri-desktop\",\"branch\":\"feature/capri-enhanced\",\"commit\":\"" + args[3] + "\",\"target\":\".NET Framework 4.8 / Windows\",\"channel\":\"development - not a public release\"}\n"));
        var manifest = new StringBuilder();
        foreach (var file in files) manifest.Append(Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant()).Append("  ").Append(file.Key).Append('\n');
        files.Add("SHA256SUMS.txt", Encoding.UTF8.GetBytes(manifest.ToString()));
        Directory.CreateDirectory(args[2]);
        string zip = Path.Combine(args[2], "Capri-Enhanced-dev-" + args[3].Substring(0, 12) + (args[3].EndsWith("-dirty") ? "-dirty" : "") + ".zip");
        using (var output = File.Create(zip))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using (var entryStream = entry.Open()) entryStream.Write(file.Value, 0, file.Value.Length);
            }
        using (var archive = ZipFile.OpenRead(zip))
        {
            if (archive.Entries.Count != files.Count) throw new InvalidDataException("Package entry count mismatch");
            foreach (var entry in archive.Entries)
            {
                using (var input = entry.Open()) using (var copy = new MemoryStream())
                { input.CopyTo(copy); if (!copy.ToArray().SequenceEqual(files[entry.FullName])) throw new InvalidDataException("ZIP content mismatch: " + entry.FullName); }
            }
        }
        string hash;
        using (var input = File.OpenRead(zip)) hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        File.WriteAllText(zip + ".sha256", hash + "  " + Path.GetFileName(zip) + "\n", new UTF8Encoding(false));
        Console.WriteLine("PASS ZIP allowlist, byte-for-byte extraction and SHA-256: " + zip);
    }
}

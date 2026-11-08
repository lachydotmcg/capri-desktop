using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CapriDesktop;
using HarfBuzzSharp;

internal static class FontBootstrapChecks
{
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlerror();

    private static bool LoadSystemHarfBuzz()
    {
        if (dlopen("libharfbuzz.so.0", 2 | 256) == IntPtr.Zero)
        {
            Console.WriteLine("SKIP system HarfBuzz is unavailable: " + Marshal.PtrToStringUTF8(dlerror()));
            return false;
        }
        Console.WriteLine("system HarfBuzz loaded GLOBAL");
        Console.Out.Flush();
        return true;
    }

    // Keep native font construction beyond the explicit bootstrap/loading boundary.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Glyphs()
    {
        for (int i = 0; i < 100; i++)
        {
            using var face = new Face((_, _) => null);
            using var font = new HarfBuzzSharp.Font(face);
            foreach (uint value in new uint[] { 65, 0x00e9, 0x5317, 0x1f600 }) font.TryGetGlyph(value, out _);
        }
        Console.WriteLine("PASS 100 font/face creation, four glyph lookups each and disposal cycles");
    }

    private static void Paths()
    {
        string root = Path.Combine(Path.GetTempPath(), "capri-font-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool absent = false;
            try { NativeFontBootstrap.BundledLibrary(root, "linux-x64"); }
            catch (FileNotFoundException) { absent = true; }
            if (!absent) throw new Exception("A missing packaged native library fell back to the system.");
            string nested = Path.Combine(root, "runtimes", "linux-x64", "native");
            Directory.CreateDirectory(nested);
            string ridFile = Path.Combine(nested, "libHarfBuzzSharp.so");
            File.WriteAllText(ridFile, "public path fixture; never loaded");
            if (NativeFontBootstrap.BundledLibrary(root, "linux-x64") != ridFile) throw new Exception("The framework-dependent RID library was not located.");
            string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string portableFolder = Path.Combine(root, "runtimes", "linux-" + architecture, "native");
            Directory.CreateDirectory(portableFolder);
            string portable = Path.Combine(portableFolder, "libHarfBuzzSharp.so");
            File.WriteAllText(portable, "public portable RID path fixture; never loaded");
            if (NativeFontBootstrap.BundledLibrary(root, "arch-" + architecture) != portable) throw new Exception("The distribution RID did not select its packaged portable Linux asset.");
            string direct = Path.Combine(root, "libHarfBuzzSharp.so");
            File.WriteAllText(direct, "public published path fixture; never loaded");
            if (NativeFontBootstrap.BundledLibrary(root, "linux-x64") != direct) throw new Exception("The published package library did not take precedence.");
            Console.WriteLine("PASS strict missing-file failure, exact and portable RID paths, published path precedence");
        }
        finally { Directory.Delete(root, true); }
    }

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux()) { Console.WriteLine("SKIP Linux native-loader check"); return 77; }
        if (args.Length != 1) throw new ArgumentException("Pass baseline-clean, baseline-global, candidate-before-global, candidate-global or paths.");
        string mode = args[0];
        if (mode == "paths") { Paths(); return 0; }
        if (mode != "baseline-clean" && mode != "baseline-global" && mode != "candidate-before-global" && mode != "candidate-global") throw new ArgumentException("Unknown font-bootstrap probe mode.");
        if (mode == "candidate-before-global") NativeFontBootstrap.Initialize();
        if (mode != "baseline-clean" && !LoadSystemHarfBuzz()) return 77;
        if (mode.StartsWith("candidate", StringComparison.Ordinal))
        {
            NativeFontBootstrap.Initialize();
            NativeFontBootstrap.Initialize();
            Console.WriteLine("candidate bootstrap initialized twice");
            Console.Out.Flush();
        }
        Glyphs();
        return 0;
    }
}

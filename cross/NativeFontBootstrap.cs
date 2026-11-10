using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // HarfBuzzSharp 8.3.1.1's net6+ loader omits the upstream LibraryLoader's Linux isolation flag.
    // Load its packaged native library first with the same flag so a system HarfBuzz, loaded by fontconfig,
    // cannot bind its own hb_* functions into this different HarfBuzz build and corrupt native font objects.
    // Source: mono/SkiaSharp PR2247, merge482e6ee2913a08a7cad76520ccf5fbce97c7c23b (MIT); issue3038.
    internal static class NativeFontBootstrap
    {
        private const int RtldNow = 2, RtldLocal = 0, RtldDeepBind = 8;
        private static readonly object gate = new object();
        // This handle owns HarfBuzzSharp's native objects for the process lifetime. Do not dlclose it.
        private static IntPtr library;

        internal static void Initialize()
        {
            if (!OperatingSystem.IsLinux()) return;
            lock (gate)
            {
                if (library != IntPtr.Zero) return;
                string path = BundledLibrary(AppContext.BaseDirectory, RuntimeInformation.RuntimeIdentifier);
                dlerror();
                IntPtr loaded = dlopen(path, RtldNow | RtldLocal | RtldDeepBind);
                if (loaded == IntPtr.Zero)
                {
                    string problem = Marshal.PtrToStringUTF8(dlerror()) ?? "the native loader did not report a reason";
                    throw new InvalidOperationException("Capri could not initialize its packaged HarfBuzzSharp library: " + path + ". " + problem);
                }
                library = loaded;
            }
        }

        internal static string BundledLibrary(string directory, string runtimeIdentifier)
        {
            string direct = Path.Combine(directory, "libHarfBuzzSharp.so");
            if (File.Exists(direct)) return Path.GetFullPath(direct);
            string nested = Path.Combine(directory, "runtimes", runtimeIdentifier, "native", "libHarfBuzzSharp.so");
            if (File.Exists(nested)) return Path.GetFullPath(nested);
            // Arch reports arch-x64, while framework-dependent NuGet assets use portable linux-x64.
            string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string portable = (runtimeIdentifier.Contains("musl", StringComparison.Ordinal) ? "linux-musl-" : "linux-") + architecture;
            string portableFile = Path.Combine(directory, "runtimes", portable, "native", "libHarfBuzzSharp.so");
            if (File.Exists(portableFile)) return Path.GetFullPath(portableFile);
            throw new FileNotFoundException("Capri's packaged HarfBuzzSharp library is missing. Restore the complete Capri package, including its native libraries.", direct);
        }

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlerror")]
        private static extern IntPtr dlerror();
    }
}

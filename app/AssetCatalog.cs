using System;
using System.IO;
using System.Reflection;
namespace CapriDesktop
{
    internal static class AssetCatalog
    {
        internal static readonly string[] Sprites = { "idle", "blink", "blink_glitch", "grin", "glitch", "dangle", "sleep", "wake", "perch", "perch_blink", "perch_blink_glitch", "perch_glitch", "mouth" };
        internal static readonly int[] Frames = { 16, 4, 4, 16, 10, 24, 16, 13, 16, 4, 4, 10, 5 };
        internal static void ValidatePng(Stream stream, int frames, string name)
        {
            if (stream == null) throw new InvalidDataException("Missing sprite: " + name);
            byte[] header = new byte[24]; int count = 0, next;
            while (count < header.Length && (next = stream.Read(header, count, header.Length - count)) > 0) count += next;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (count != 24) throw new InvalidDataException("Truncated sprite: " + name);
            for (int i = 0; i < signature.Length; i++) if (header[i] != signature[i]) throw new InvalidDataException("Invalid PNG sprite: " + name);
            long width = Number(header, 16), height = Number(header, 20);
            if (header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82 || frames <= 0 || width < frames || width % frames != 0 || height <= 0 || width > 65536 || height > 8192)
                throw new InvalidDataException("Invalid sprite strip geometry: " + name);
        }
        private static long Number(byte[] data, int at) { return ((long)data[at] << 24) | ((long)data[at + 1] << 16) | ((long)data[at + 2] << 8) | data[at + 3]; }
        internal static void Validate(Assembly assembly)
        {
            for (int i = 0; i < Sprites.Length; i++)
                using (Stream stream = assembly.GetManifestResourceStream("Capri.Sprite." + Sprites[i] + ".png")) ValidatePng(stream, Frames[i], Sprites[i]);
            foreach (string name in new[] { "Capri.Chip.ico", "Capri.Font.Mono.ttf" })
                using (Stream stream = assembly.GetManifestResourceStream(name)) if (stream == null || stream.Length == 0) throw new InvalidDataException("Missing embedded resource: " + name);
        }
    }
}

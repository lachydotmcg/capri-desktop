using System;
using System.Drawing;
using System.IO;

namespace CapriDesktop
{
    internal sealed class ClipSettings
    {
        internal static readonly int[] Lengths = { 15, 30, 45, 60, 90, 120 };
        internal static readonly int[] Rates = { 15, 24, 30, 60 };
        internal static readonly int[] Sizes = { 0, 480, 720, 1080 };
        internal static readonly int[] Bitrates = { 0, 2, 4, 8, 12, 20 };
        internal readonly string Folder;
        internal readonly int Seconds, Fps, Resolution, Mbps;

        internal ClipSettings(string folder = null, int seconds = 30, int fps = 30, int resolution = 0, int mbps = 0)
        {
            Folder = string.IsNullOrWhiteSpace(folder) ? ClipBuffer.Folder : Path.GetFullPath(folder);
            Seconds = Pick(Lengths, seconds, 30);
            Fps = Pick(Rates, fps, 30);
            Resolution = Pick(Sizes, resolution, 0);
            Mbps = Pick(Bitrates, mbps, 0);
        }

        private static int Pick(int[] choices, int value, int fallback) { return Array.IndexOf(choices, value) < 0 ? fallback : value; }

        internal static ClipSettings Read(Preferences saved)
        {
            try { return new ClipSettings(saved.ClipFolder, saved.ClipSeconds, saved.ClipFps, saved.ClipResolution, saved.ClipMbps); }
            catch (ArgumentException) { return new ClipSettings(null, saved.ClipSeconds, saved.ClipFps, saved.ClipResolution, saved.ClipMbps); }
            catch (NotSupportedException) { return new ClipSettings(null, saved.ClipSeconds, saved.ClipFps, saved.ClipResolution, saved.ClipMbps); }
            catch (PathTooLongException) { return new ClipSettings(null, saved.ClipSeconds, saved.ClipFps, saved.ClipResolution, saved.ClipMbps); }
        }

        internal Size Fit(int width, int height)
        {
            int tall = Resolution == 0 ? 1080 : Resolution;
            double scale = Math.Min(1, Math.Min(tall * (16.0 / 9) / width, (double)tall / height));
            return new Size(Math.Max(2, ((int)(width * scale)) & ~1), Math.Max(2, ((int)(height * scale)) & ~1));
        }

        internal int Bits(int width, int height) { return Mbps == 0 ? Math.Max(250000, Math.Min(12000000, width * height * 4)) : Mbps * 1000000; }
    }

    internal partial class Companion
    {
        internal ClipSettings ClipOptions { get { return ClipSettings.Read(preferences); } }

        internal void SetClipOptions(int seconds, int fps, int resolution, int mbps)
        {
            ClipSettings chosen = new ClipSettings(ClipOptions.Folder, seconds, fps, resolution, mbps);
            preferences.ClipSeconds = chosen.Seconds;
            preferences.ClipFps = chosen.Fps;
            preferences.ClipResolution = chosen.Resolution;
            preferences.ClipMbps = chosen.Mbps;
            clips.Configure(chosen);
            SavePreferences();
            mainframe.Sync();
        }

        internal string SetClipFolder(string path)
        {
            try
            {
                ClipSettings chosen = new ClipSettings(path);
                if (!Directory.Exists(chosen.Folder)) return "Pick an existing folder for your clips.";
                preferences.ClipFolder = chosen.Folder;
                clips.Configure(ClipOptions);
                SavePreferences();
                mainframe.Sync();
                return null;
            }
            catch (Exception error) { return "Couldn't use that folder: " + error.Message; }
        }
    }
}

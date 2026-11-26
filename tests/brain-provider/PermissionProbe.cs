using System;
using System.IO;

internal static class PermissionProbe
{
    private static bool? enforced;

    internal static bool Enforced()
    {
        if (enforced.HasValue) return enforced.Value;
        enforced = Probe();
        return enforced.Value;
    }

    private static bool Probe()
    {
        string folder = Path.Combine(Path.GetTempPath(), "capri-mode-probe-" + Guid.NewGuid().ToString("N"));
        bool enforced = false;
        try
        {
            Directory.CreateDirectory(folder);
            UnixFileMode mode = File.GetUnixFileMode(folder);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { File.WriteAllText(Path.Combine(folder, "probe"), "x"); }
            catch (Exception) { enforced = true; }
            finally { File.SetUnixFileMode(folder, mode); }
        }
        catch (Exception) { enforced = false; }
        finally { try { Directory.Delete(folder, true); } catch (Exception) { } }
        return enforced;
    }
}

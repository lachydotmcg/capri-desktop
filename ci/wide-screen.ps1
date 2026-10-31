param([int]$Width = 1920, [int]$Height = 1080)
# GitHub's Windows machine starts at 1024 x 768, smaller than her windows at 150% and 200%, so her test gets a real 1080p desktop.
# It says what it finds and never stops the run: her test is what decides whether the screen was big enough.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WideScreen
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumDisplaySettings(string device, int mode, ref Mode found);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref Mode mode, IntPtr window, int flags, IntPtr parameter);
    private static Mode Blank() { Mode mode = new Mode(); mode.Size = (short)Marshal.SizeOf(typeof(Mode)); return mode; }
    public static string Sizes(string device)
    {
        List<string> sizes = new List<string>();
        Mode mode = Blank();
        for (int index = 0; EnumDisplaySettings(device, index, ref mode); index++) { string size = mode.Width + "x" + mode.Height; if (!sizes.Contains(size)) sizes.Add(size); }
        return sizes.Count > 0 ? string.Join(", ", sizes.ToArray()) : "none, error " + Marshal.GetLastWin32Error();
    }
    public static string Set(string device, int width, int height)
    {
        Mode mode = Blank();
        if (!EnumDisplaySettings(device, -1, ref mode)) return "no current mode, error " + Marshal.GetLastWin32Error();
        mode.Width = width;
        mode.Height = height;
        mode.Fields = 0x80000 | 0x100000;
        return "ChangeDisplaySettingsEx returned " + ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, 1, IntPtr.Zero);
    }
}
'@
# A fresh process each time, because .NET keeps the size it first read.
function Screen { powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; `$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; '' + `$bounds.Width + ' x ' + `$bounds.Height" }
$device = [System.Windows.Forms.Screen]::PrimaryScreen.DeviceName
'Screen at the start: ' + (Screen) + ' on ' + $device + ', session ' + (Get-Process -Id $PID).SessionId + ', interactive ' + [Environment]::UserInteractive
Get-CimInstance Win32_VideoController | ForEach-Object { 'Adapter: ' + $_.Name + ' at ' + $_.CurrentHorizontalResolution + ' x ' + $_.CurrentVerticalResolution }
'Sizes this display offers: ' + [WideScreen]::Sizes($device)
[WideScreen]::Set($device, $Width, $Height)
Start-Sleep -Seconds 1
if ((Screen) -ne "$Width x $Height") {
    try { Set-DisplayResolution -Width $Width -Height $Height -Force -ErrorAction Stop; 'Set-DisplayResolution ran' }
    catch { 'Set-DisplayResolution failed: ' + $_.Exception.Message }
    Start-Sleep -Seconds 2
}
'Screen now: ' + (Screen)
exit 0

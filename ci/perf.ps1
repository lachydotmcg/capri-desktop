param(
    [int]$Id,
    [ValidateRange(1, 86400)][int]$Seconds = 10,
    [string]$Scene = 'idle',
    [string]$Log = (Join-Path $PSScriptRoot 'perf.log')
)
$ErrorActionPreference = 'Stop'
if (-not ('CapriPerf.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace CapriPerf {
    public static class Native {
        [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] public static extern bool EnumWindows(Window next, IntPtr state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
        public delegate bool Window(IntPtr window, IntPtr state);
        public static uint UiThread(int id) {
            System.Collections.Generic.HashSet<uint> found = new System.Collections.Generic.HashSet<uint>();
            EnumWindows(delegate(IntPtr window, IntPtr state) {
                uint owner;
                uint thread = GetWindowThreadProcessId(window, out owner);
                if (owner != id) return true;
                System.Text.StringBuilder name = new System.Text.StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                if (!name.ToString().StartsWith("WindowsForms10.")) return true;
                found.Add(thread);
                return true;
            }, IntPtr.Zero);
            if (found.Count == 1) foreach (uint thread in found) return thread;
            return 0;
        }
    }
}
'@
}
function Read-Threads($Process) {
    $found = @{}
    $Process.Refresh()
    foreach ($thread in $Process.Threads) {
        try {
            $found[$thread.Id] = [pscustomobject]@{
                Cpu = $thread.TotalProcessorTime.TotalMilliseconds
                Start = $thread.StartTime.ToUniversalTime().Ticks
            }
        } catch [System.InvalidOperationException] {
        } catch [System.ComponentModel.Win32Exception] {
        } finally { $thread.Dispose() }
    }
    return $found
}
if ($PSBoundParameters.ContainsKey('Id')) {
    $target = Get-Process -Id $Id
    if ($target.ProcessName -ne 'Capri') { $target.Dispose(); throw 'That process is not Capri.exe.' }
} else {
    $choices = @(Get-Process -Name Capri -ErrorAction SilentlyContinue | Sort-Object Id)
    if ($choices.Count -eq 0) { throw 'Capri.exe is not running.' }
    if ($choices.Count -eq 1) { $target = $choices[0] }
    else {
        $windows = @($choices | Where-Object { [CapriPerf.Native]::UiThread($_.Id) -ne 0 })
        if ($windows.Count -ne 1) {
            foreach ($choice in $choices) { $choice.Dispose() }
            throw 'Several Capri processes are running. Use -Id to choose one.'
        }
        $target = $windows[0]
        foreach ($choice in $choices) { if ($choice.Id -ne $target.Id) { $choice.Dispose() } }
    }
}
try {
    $ui = 0
    if ($target.MainWindowHandle -ne [IntPtr]::Zero) {
        [uint32]$owner = 0
        $ui = [CapriPerf.Native]::GetWindowThreadProcessId($target.MainWindowHandle, [ref]$owner)
        if ($owner -ne $target.Id) { $ui = 0 }
    }
    if ($ui -eq 0) { $ui = [CapriPerf.Native]::UiThread($target.Id) }
    $before = Read-Threads $target
    $target.Refresh()
    $cpuStart = $target.TotalProcessorTime.TotalMilliseconds
    $privateStart = $target.PrivateMemorySize64
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $Seconds
    $target.Refresh()
    if ($target.HasExited) { throw 'Capri exited during the sample.' }
    $cpuEnd = $target.TotalProcessorTime.TotalMilliseconds
    $elapsed = $watch.Elapsed.TotalSeconds
    $working = $target.WorkingSet64
    $privateEnd = $target.PrivateMemorySize64
    $handles = $target.HandleCount
    $gdi = [CapriPerf.Native]::GetGuiResources($target.Handle, 0)
    $user = [CapriPerf.Native]::GetGuiResources($target.Handle, 1)
    $after = Read-Threads $target
    $rows = foreach ($threadId in $after.Keys) {
        $first = $before[$threadId]
        $last = $after[$threadId]
        $fresh = $null -eq $first -or $first.Start -ne $last.Start
        $cpu = if ($fresh) { $last.Cpu } else { [Math]::Max(0, $last.Cpu - $first.Cpu) }
        [pscustomobject]@{
            Thread = $threadId
            Role = if ($threadId -eq $ui) { 'UI' } elseif ($fresh) { 'new' } else { '' }
            CpuMs = [Math]::Round($cpu, 2)
            CorePercent = [Math]::Round($cpu / ($elapsed * 10), 2)
        }
    }
    $rows = @($rows | Sort-Object CpuMs -Descending)
    $core = ($cpuEnd - $cpuStart) / ($elapsed * 10)
    $machine = $core / [Environment]::ProcessorCount
    Write-Output ('Capri {0}, {1}, {2:0.00} s' -f $target.Id, $Scene, $elapsed)
    $rows | Format-Table -AutoSize
    Write-Output ('Process CPU {0:0.00}% of all CPUs, {1:0.00}% of one core' -f $machine, $core)
    Write-Output ('Working set {0:0.00} MB, private {1:0.00} -> {2:0.00} MB, handles {3}, GDI {4}, USER {5}' -f ($working / 1MB), ($privateStart / 1MB), ($privateEnd / 1MB), $handles, $gdi, $user)
    if ($ui -eq 0) { Write-Output 'UI thread could not be identified.' }
    $ended = @($before.Keys | Where-Object { -not $after.ContainsKey($_) })
    if ($ended.Count -gt 0) { Write-Output ('Threads that ended during the sample have no final CPU reading: ' + ($ended -join ',')) }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $threadLog = ($rows | ForEach-Object { '{0}/{1}/{2}' -f $_.Thread, $_.Role, $_.CpuMs.ToString('0.00', $culture) }) -join ','
    $line = [string]::Format($culture, '{0:o} pid={1} scene="{2}" seconds={3:0.000} process_cpu_ms={4:0.00} process_cpu_pct={5:0.00} core_cpu_pct={6:0.00} working_mb={7:0.00} private_mb_start={8:0.00} private_mb_end={9:0.00} handles={10} gdi={11} user={12} ui_thread={13} threads="{14}" ended_threads="{15}"', [DateTime]::UtcNow, $target.Id, ($Scene -replace '[\r\n"]', ' '), $elapsed, ($cpuEnd - $cpuStart), $machine, $core, ($working / 1MB), ($privateStart / 1MB), ($privateEnd / 1MB), $handles, $gdi, $user, $ui, $threadLog, ($ended -join ','))
    $line += ' working_bytes={0} private_bytes_start={1} private_bytes_end={2}' -f $working, $privateStart, $privateEnd
    Add-Content -LiteralPath $Log -Value $line -Encoding UTF8
} finally { $target.Dispose() }

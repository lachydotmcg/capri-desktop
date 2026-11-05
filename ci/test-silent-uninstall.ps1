param([Parameter(Mandatory=$true)][string]$Setup)

# Removes the Capri that test-silent-install.ps1 put in this disposable profile, with no window, the way a package
# manager does. It only runs in a throwaway profile, with CAPRI_DISPOSABLE_USER=1.
$ErrorActionPreference = 'Stop'
if ($env:CAPRI_DISPOSABLE_USER -ne '1') { throw 'Set CAPRI_DISPOSABLE_USER=1 in a disposable Windows profile.' }

$setup = (Resolve-Path $Setup).Path
$capriDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Capri'
$app = Join-Path $capriDir 'Capri.exe'
$uninstaller = Join-Path $capriDir 'Uninstall.exe'
$marker = Join-Path $capriDir '.capri-install'
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'Capri.lnk'
$otherLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Capri.lnk'
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri.Desktop'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (!(Test-Path $marker) -or !(Test-Path $uninstaller)) { throw 'Run test-silent-install.ps1 first.' }
if (Test-Path $otherLink) { throw "A desktop shortcut is already at $otherLink." }

# Her packs as she downloads them, a Start with Windows entry for her, and things that aren't hers: the user's own
# files and mods in her folder, and a desktop shortcut called Capri that starts something else.
$packs = 'brain', 'voice', 'ears', 'wake'
foreach ($pack in $packs) {
    New-Item (Join-Path $capriDir "$pack\model") -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $capriDir "$pack\model\fixture"), $pack)
}
New-Item (Join-Path $capriDir 'mods') -ItemType Directory -Force | Out-Null
$kept = (Join-Path $capriDir 'kept.txt'), (Join-Path $capriDir 'mods\kept.capri-mod')
foreach ($file in $kept) { [IO.File]::WriteAllText($file, 'theirs') }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($otherLink)
$shortcut.TargetPath = Join-Path $env:WINDIR 'notepad.exe'
$shortcut.Save()
# A fresh runner has no Run key until something starts with Windows.
if (!(Test-Path $run)) { New-Item $run | Out-Null }
Set-ItemProperty $run -Name Capri -Value ('"' + $app + '"')

$err = Join-Path $env:TEMP ('capri-uninstall-' + [guid]::NewGuid().ToString('N') + '.txt')
function Run([string]$exe, [string]$flags) {
    $p = Start-Process $exe -ArgumentList $flags -Wait -PassThru -RedirectStandardError $err
    return @($p.ExitCode, (Get-Content $err -Raw -ErrorAction SilentlyContinue))
}
function Kept {
    foreach ($file in $kept) { if (!(Test-Path $file)) { throw "The uninstall removed $file, which isn't hers." } }
    if (!(Test-Path $otherLink)) { throw 'The uninstall removed a shortcut that starts another program.' }
}
try {
    # A file Windows won't let go of fails the uninstall with its reason, after her Start with Windows entry is gone,
    # and keeps her install marker and uninstall entry, so her setup can try again.
    $lock = [IO.File]::Open($app, 'Open', 'Read', 'Read')
    try { $failed = Run $uninstaller '--uninstall --silent' } finally { $lock.Dispose() }
    if ($failed[0] -eq 0) { throw 'A locked Capri.exe was reported as removed.' }
    if ([string]::IsNullOrWhiteSpace($failed[1])) { throw 'The silent uninstall failed without a reason.' }
    if (!(Test-Path $marker) -or !(Test-Path $arp) -or !(Test-Path $app) -or !(Test-Path $uninstaller)) { throw 'A failed uninstall lost her install marker, uninstall entry, app or uninstaller, so it cannot be tried again.' }
    if ($null -ne (Get-ItemProperty $run -Name Capri -ErrorAction SilentlyContinue)) { throw 'A failed uninstall kept her Start with Windows entry.' }
    Kept

    # Trying again goes through her uninstall entry's quiet command, the way winget removes her.
    $quiet = (Get-ItemProperty $arp -Name QuietUninstallString -ErrorAction SilentlyContinue).QuietUninstallString
    if ($quiet -notmatch '^"([^"]+)" (.+)$' -or $Matches[1] -ne $uninstaller) { throw "Her uninstall entry's quiet command doesn't run her uninstaller: $quiet" }
    $retry = Run $Matches[1] $Matches[2]
    if ($retry[0] -ne 0) { throw "Trying again through her uninstall entry exited $($retry[0]): $($retry[1])" }
    foreach ($gone in $app, $uninstaller, $marker, $link, $arp) { if (Test-Path $gone) { throw "$gone remains after the uninstall." } }
    foreach ($pack in $packs) { if (Test-Path (Join-Path $capriDir $pack)) { throw "Her $pack pack remains after the uninstall." } }
    Kept

    $nothing = Run $setup '--silent --uninstall'
    if ($nothing[0] -eq 0) { throw 'Removing a Capri that is not installed was reported as removed.' }
    if ([string]::IsNullOrWhiteSpace($nothing[1])) { throw 'Removing a Capri that is not installed failed without a reason.' }
    Kept

    # She installs again over what's left, and her own Uninstall.exe removes her with no window.
    $installed = Run $setup '--silent'
    if ($installed[0] -ne 0) { throw "Installing again exited $($installed[0]): $($installed[1])" }
    $removed = Run $uninstaller '--uninstall --silent'
    if ($removed[0] -ne 0) { throw "Uninstall.exe --uninstall --silent exited $($removed[0]): $($removed[1])" }
    foreach ($gone in $app, $uninstaller, $marker, $link, $arp) { if (Test-Path $gone) { throw "$gone remains after Uninstall.exe removed her." } }
    Kept
    Write-Output 'PASS silent uninstall: a locked app fails with a reason, clears her startup entry and stays retryable from her own uninstall entry; trying again through its quiet command removes her app, uninstaller, marker, brain, voice, ears and wake packs, Start menu shortcut and uninstall entry; removing her when she is not installed fails with a reason; her own Uninstall.exe removes a fresh install; the user''s files, mods and a shortcut to another program stay every time'
}
finally {
    if (Test-Path $err) { Remove-Item $err -Force }
    if (Test-Path $otherLink) { Remove-Item $otherLink -Force }
    if ((Get-ItemProperty $run -Name Capri -ErrorAction SilentlyContinue).Capri -eq ('"' + $app + '"')) { Remove-ItemProperty $run -Name Capri }
}

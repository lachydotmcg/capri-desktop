param([Parameter(Mandatory=$true)][string]$Setup)

# Installs Capri with no window, the way a package manager runs her setup. It changes this Windows user's own install,
# so it only runs in a throwaway profile like a CI runner's, with CAPRI_DISPOSABLE_USER=1, and never over a Capri that's
# already there.
$ErrorActionPreference = 'Stop'
if ($env:CAPRI_DISPOSABLE_USER -ne '1') { throw 'Set CAPRI_DISPOSABLE_USER=1 in a disposable Windows profile.' }

$setup = (Resolve-Path $Setup).Path
$capriDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Capri'
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'Capri.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Capri.lnk'
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri.Desktop'
foreach ($installed in (Join-Path $capriDir '.capri-install'), (Join-Path $capriDir 'Capri.exe'), (Join-Path $capriDir 'Uninstall.exe'), $link, $desktopLink, $arp) {
    if (Test-Path $installed) { throw "Capri is already installed for this user: $installed" }
}

$err = Join-Path $env:TEMP ('capri-install-' + [guid]::NewGuid().ToString('N') + '.txt')
function Run([string]$flags) {
    $p = Start-Process $setup -ArgumentList $flags -Wait -PassThru -RedirectStandardError $err
    return @($p.ExitCode, (Get-Content $err -Raw -ErrorAction SilentlyContinue))
}
try {
    $done = Run '--silent'
    if ($done[0] -ne 0) { throw "Capri-Setup.exe --silent exited $($done[0]): $($done[1])" }
    foreach ($file in 'Capri.exe', 'Uninstall.exe', '.capri-install') {
        if (!(Test-Path (Join-Path $capriDir $file))) { throw "$file is missing after a silent install." }
    }
    if (!(Test-Path $link)) { throw 'The Start menu shortcut is missing.' }
    if (Test-Path $desktopLink) { throw 'The silent install added a desktop shortcut.' }
    $entry = Get-ItemProperty $arp
    if ($entry.UninstallString -ne ('"' + (Join-Path $capriDir 'Uninstall.exe') + '" --uninstall')) { throw "The uninstall entry runs $($entry.UninstallString)." }
    if (Test-Path (Join-Path $capriDir 'voice')) { throw 'The silent install downloaded the speech model.' }
    if (@(Get-Process Capri -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$capriDir\*" }).Count -ne 0) { throw 'The silent install opened Capri.' }

    $again = Run '--silent'
    if ($again[0] -ne 0) { throw "A second silent install exited $($again[0]): $($again[1])" }
    if (!(Test-Path (Join-Path $capriDir 'Capri.exe')) -or !(Test-Path $link)) { throw 'A second silent install lost her app or shortcut.' }

    foreach ($flags in '--silent --unknown', '--silent --update', '--silent --remove-files') {
        $wrong = Run $flags
        if ($wrong[0] -eq 0) { throw "Capri-Setup.exe $flags was accepted." }
        if ([string]::IsNullOrWhiteSpace($wrong[1])) { throw "Capri-Setup.exe $flags failed without a reason." }
    }
    if (!(Test-Path (Join-Path $capriDir '.capri-install'))) { throw 'A refused command removed her.' }
    Write-Output 'PASS silent install: app, uninstaller, marker, Start menu shortcut and uninstall entry, no desktop shortcut, speech model or launch; a second run installs over it, and a wrong flag fails with a reason instead of opening a window'
}
finally {
    if (Test-Path $err) { Remove-Item $err -Force }
}

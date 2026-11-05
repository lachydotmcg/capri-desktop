param([Parameter(Mandatory=$true)][string]$Setup)

$ErrorActionPreference = 'Stop'
if ($env:CAPRI_DISPOSABLE_USER -ne '1') { throw 'Set CAPRI_DISPOSABLE_USER=1 in a disposable Windows profile.' }

$setup = (Resolve-Path $Setup).Path
$capriDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Capri'
$marker = Join-Path $capriDir 'Capri.package'
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'Capri.lnk'
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri.Desktop'
foreach ($installed in (Join-Path $capriDir '.capri-install'), (Join-Path $capriDir 'Capri.exe'), $marker, $link, $arp) {
    if (Test-Path $installed) { throw "Capri is already installed for this user: $installed" }
}

$err = Join-Path $env:TEMP ('capri-package-' + [guid]::NewGuid().ToString('N') + '.txt')
function Run([string]$flags) {
    $p = Start-Process $setup -ArgumentList $flags -Wait -PassThru -RedirectStandardError $err
    if ($p.ExitCode -ne 0) { throw "Capri-Setup.exe $flags exited $($p.ExitCode): $(Get-Content $err -Raw)" }
}
try {
    Run '--silent --package winget'
    if (!(Test-Path $marker)) { throw 'The package marker is missing.' }
    if ((Get-Content $marker -Raw).Trim() -ne 'winget') { throw 'The package marker is not winget.' }
    Run '--silent'
    if (!(Test-Path $marker)) { throw 'The manual marker is missing.' }
    if ((Get-Content $marker -Raw).Trim() -ne 'manual') { throw 'A plain silent install did not free her updater.' }
    foreach ($flags in '--silent --package apt', '--silent --package 0', '--silent --package -', '--package winget', '--uninstall --silent --package winget') {
        $before = (Get-FileHash $marker -Algorithm SHA256).Hash
        $p = Start-Process $setup -ArgumentList $flags -Wait -PassThru -RedirectStandardError $err
        if ($p.ExitCode -ne 2) { throw "Capri-Setup.exe $flags exited $($p.ExitCode)." }
        if ((Get-FileHash $marker -Algorithm SHA256).Hash -ne $before) { throw "Capri-Setup.exe $flags changed the marker." }
    }
    if (!(Test-Path (Join-Path $capriDir '.capri-install'))) { throw 'A refused command removed her.' }
    Run '--silent --package scoop'
    if ((Get-Content $marker -Raw).Trim() -ne 'scoop') { throw 'The package marker is not scoop.' }
    $p = Start-Process (Join-Path $capriDir 'Uninstall.exe') -ArgumentList '--uninstall --silent' -Wait -PassThru -RedirectStandardError $err
    if ($p.ExitCode -ne 0) { throw "Uninstall.exe --uninstall --silent exited $($p.ExitCode): $(Get-Content $err -Raw)" }
    if (Test-Path $marker) { throw 'Removing her left her package marker.' }
    if ((Test-Path (Join-Path $capriDir '.capri-install')) -or (Test-Path $arp)) { throw 'The silent uninstall left her installed.' }
    Write-Output 'PASS package marker: --silent --package winget writes winget, a plain --silent writes manual, unknown names and --package without a silent install exit 2 and leave it alone, and a silent uninstall removes it'
}
finally {
    if (Test-Path $err) { Remove-Item $err -Force }
}

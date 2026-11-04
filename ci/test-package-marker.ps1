param([Parameter(Mandatory=$true)][string]$Setup)

$ErrorActionPreference = 'Stop'
if ($env:CAPRI_DISPOSABLE_USER -ne '1') { throw 'Set CAPRI_DISPOSABLE_USER=1 in a disposable Windows profile.' }

$setup = (Resolve-Path $Setup).Path
$capriDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Capri'
$marker = Join-Path $capriDir 'Capri.package'
$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'Capri.lnk'
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Capri.Desktop'
if (Test-Path $capriDir) { throw "Capri is already installed at $capriDir." }
if (Test-Path $link) { throw "Capri's shortcut already exists at $link." }
if (Test-Path $arp) { throw "Capri's uninstall entry already exists." }

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
    if (!(Test-Path $marker)) { throw 'The generic package marker is missing.' }
    if ((Get-Content $marker -Raw).Trim() -ne 'package') { throw 'A plain silent install did not write the generic marker.' }
    foreach ($n in 'apt','0','-') {
        $before = (Get-FileHash $marker -Algorithm SHA256).Hash
        $p = Start-Process $setup -ArgumentList "--silent --package $n" -Wait -PassThru -RedirectStandardError $err
        if ($p.ExitCode -ne 2) { throw "Invalid manager $n exited $($p.ExitCode)." }
        if ((Get-FileHash $marker -Algorithm SHA256).Hash -ne $before) { throw "Invalid manager $n changed the marker." }
    }
    Write-Output 'PASS package marker: --silent --package winget writes winget, a plain --silent writes package, and invalid names are rejected'
}
finally {
    if (Test-Path $err) { Remove-Item $err -Force }
}

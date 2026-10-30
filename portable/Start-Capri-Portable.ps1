[CmdletBinding()]
param([string]$Model,[switch]$SkipModelDownload)
$ErrorActionPreference = 'Stop'
$usbRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $usbRoot 'Capri.exe'
$dataRoot = Join-Path $usbRoot 'CAPRI-Data'
$modelRoot = Join-Path $dataRoot 'Models'
$logRoot = Join-Path $dataRoot 'Logs'
$ollamaLog = Join-Path $logRoot 'ollama.log'
function Write-Status([string]$Message) { Write-Host "[CAPRI USB] $Message" -ForegroundColor Cyan }
function Stop-WithHelp([string]$Message) {
    Write-Host $Message -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}
if (-not (Test-Path -LiteralPath $app)) { Stop-WithHelp 'Capri.exe is missing. Extract the complete CAPRI-Portable package to the root of your USB drive.' }
if ($env:OS -ne 'Windows_NT') { Stop-WithHelp 'This portable launcher currently supports Windows 10 and 11.' }
New-Item -ItemType Directory -Force -Path $dataRoot,$modelRoot,$logRoot,(Join-Path $dataRoot 'Memory'),(Join-Path $dataRoot 'Settings'),(Join-Path $dataRoot 'Downloads') | Out-Null
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($usbRoot))
if (-not $drive.IsReady) { Stop-WithHelp 'The USB drive is not ready.' }
Write-Status "USB drive: $($drive.Name)"
Write-Status ("Free space: {0:N1} GB" -f ($drive.AvailableFreeSpace / 1GB))
$env:OLLAMA_MODELS = $modelRoot
$env:OLLAMA_HOST = '127.0.0.1:11434'
$env:CAPRI_PORTABLE_HOME = $dataRoot
$ollama = Get-Command 'ollama.exe' -ErrorAction SilentlyContinue
if (-not $ollama) { Stop-WithHelp 'Ollama is not installed on this PC. Install the free Ollama Windows runtime once, then run this launcher again. CAPRI models and portable downloads will be stored on this USB drive.' }
$listener = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 11434 -State Listen -ErrorAction SilentlyContinue
if ($listener) { Stop-WithHelp 'An Ollama server is already listening on port 11434. Close Ollama from the system tray, then relaunch CAPRI from this USB so its model directory can be set to the USB drive.' }
Write-Status 'Starting a private Ollama server with its model directory on this USB...'
$server = Start-Process -FilePath $ollama.Source -ArgumentList 'serve' -PassThru -WindowStyle Hidden -RedirectStandardOutput $ollamaLog -RedirectStandardError $ollamaLog
try {
    $ready = $false
    for ($i=0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        if ($server.HasExited) { throw "Ollama stopped during startup. See $ollamaLog" }
        try { Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; $ready = $true; break } catch {}
    }
    if (-not $ready) { throw "Ollama did not become ready. See $ollamaLog" }
    if (-not $SkipModelDownload) {
        if ([string]::IsNullOrWhiteSpace($Model)) {
            $ram = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
            if ($ram -ge 16GB) { $Model = 'gemma3:4b' } else { $Model = 'gemma3:1b' }
        }
        if ($drive.AvailableFreeSpace -lt 5GB) { throw 'At least 5 GB of free USB storage is required before downloading a model.' }
        $existing = (& $ollama.Source list 2>$null | Out-String)
        if ($existing -notmatch [regex]::Escape($Model)) {
            Write-Status "Downloading free local model $Model directly to the USB drive. This can take several minutes."
            & $ollama.Source pull $Model
            if ($LASTEXITCODE -ne 0) { throw "Ollama could not download $Model. Check internet connection and USB free space." }
        } else { Write-Status "Model $Model is already present on this USB drive." }
    }
    Write-Status 'Launching CAPRI from the USB drive...'
    $process = Start-Process -FilePath $app -WorkingDirectory $usbRoot -PassThru
    Write-Status "CAPRI is running. Model storage: $modelRoot"
    Write-Status 'Keep the USB connected until CAPRI is closed. Close CAPRI before safely ejecting the drive.'
    $process.WaitForExit()
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Read-Host 'Press Enter to close'
    exit 1
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
}

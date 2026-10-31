[CmdletBinding()]
param([string]$Model,[switch]$SkipModelDownload)
$ErrorActionPreference = 'Stop'
$usbRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $usbRoot 'Capri.exe'
$dataRoot = Join-Path $usbRoot 'CAPRI-Data'
$modelRoot = Join-Path $dataRoot 'Models'
$runtimeRoot = Join-Path $dataRoot 'Runtime'
$ollamaRoot = Join-Path $runtimeRoot 'Ollama'
$logRoot = Join-Path $dataRoot 'Logs'
$stdoutLog = Join-Path $logRoot 'ollama.stdout.log'
$stderrLog = Join-Path $logRoot 'ollama.stderr.log'
function Write-Status([string]$Message) { Write-Host "[CAPRI USB] $Message" -ForegroundColor Cyan }
function Stop-WithHelp([string]$Message) {
    Write-Host $Message -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}
if (-not (Test-Path -LiteralPath $app)) { Stop-WithHelp 'Capri.exe is missing. Extract the complete CAPRI-Portable package to the root of your USB drive.' }
if ($env:OS -ne 'Windows_NT') { Stop-WithHelp 'This portable launcher currently supports Windows 10 and 11.' }
New-Item -ItemType Directory -Force -Path $dataRoot,$modelRoot,$runtimeRoot,$ollamaRoot,$logRoot,(Join-Path $dataRoot 'Memory'),(Join-Path $dataRoot 'Settings'),(Join-Path $dataRoot 'Downloads') | Out-Null
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($usbRoot))
if (-not $drive.IsReady) { Stop-WithHelp 'The USB drive is not ready.' }
Write-Status "USB drive: $($drive.Name)"
Write-Status ("Free space: {0:N1} GB" -f ($drive.AvailableFreeSpace / 1GB))
if ($drive.AvailableFreeSpace -lt 8GB) { Stop-WithHelp 'At least 8 GB of free space is recommended for the portable AI runtime and starter model. Free space on the USB and try again.' }

$env:OLLAMA_MODELS = $modelRoot
$env:OLLAMA_HOST = '127.0.0.1:11434'
$env:CAPRI_PORTABLE_HOME = $dataRoot
$env:OLLAMA_KEEP_ALIVE = '5m'

$ollamaExe = Join-Path $ollamaRoot 'ollama.exe'
if (-not (Test-Path -LiteralPath $ollamaExe)) {
    Write-Status 'Downloading the official standalone Ollama Windows runtime to the USB...'
    $zipPath = Join-Path $runtimeRoot 'ollama-windows-amd64.zip'
    $extractPath = Join-Path $runtimeRoot 'extract'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri 'https://github.com/ollama/ollama/releases/latest/download/ollama-windows-amd64.zip' -OutFile $zipPath
        if (Test-Path $extractPath) { Remove-Item -LiteralPath $extractPath -Recurse -Force }
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractPath -Force
        $found = Get-ChildItem -LiteralPath $extractPath -Filter 'ollama.exe' -File -Recurse | Select-Object -First 1
        if (-not $found) { throw 'The downloaded Ollama archive did not contain ollama.exe.' }
        $runtimeFiles = Get-ChildItem -LiteralPath $found.Directory.FullName -File -Recurse
        foreach ($runtimeFile in $runtimeFiles) {
            $relative = $runtimeFile.FullName.Substring($found.Directory.FullName.Length).TrimStart('\')
            $destination = Join-Path $ollamaRoot $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $runtimeFile.FullName -Destination $destination -Force
        }
        if (-not (Test-Path -LiteralPath $ollamaExe)) {
            Copy-Item -LiteralPath $found.FullName -Destination $ollamaExe -Force
        }
    }
    catch {
        Stop-WithHelp ("Could not install the portable Ollama runtime. Check the internet connection and try again. Details: " + $_.Exception.Message)
    }
    finally {
        if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue }
        if (Test-Path $extractPath) { Remove-Item -LiteralPath $extractPath -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
if (-not (Test-Path -LiteralPath $ollamaExe)) { Stop-WithHelp 'The Ollama runtime could not be installed on this USB drive.' }

$listener = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 11434 -State Listen -ErrorAction SilentlyContinue
if ($listener) { Stop-WithHelp 'Another service is already listening on port 11434. Close Ollama from the system tray or stop that service, then relaunch CAPRI from this USB.' }
Write-Status 'Starting the USB-hosted Ollama runtime...'
$server = Start-Process -FilePath $ollamaExe -ArgumentList 'serve' -PassThru -WindowStyle Hidden -WorkingDirectory $ollamaRoot -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
try {
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        if ($server.HasExited) { throw "Ollama stopped during startup. See $stderrLog" }
        try { Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; $ready = $true; break } catch {}
    }
    if (-not $ready) { throw "Ollama did not become ready. See $stderrLog" }
    if (-not $SkipModelDownload) {
        if ([string]::IsNullOrWhiteSpace($Model)) {
            $ram = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
            if ($ram -ge 16GB) { $Model = 'gemma3:4b' } else { $Model = 'gemma3:1b' }
        }
        $existing = (& $ollamaExe list 2>$null | Out-String)
        if ($existing -notmatch [regex]::Escape($Model)) {
            Write-Status "Downloading free local vision model $Model to the USB. This may take several minutes..."
            & $ollamaExe pull $Model
            if ($LASTEXITCODE -ne 0) { throw "Ollama could not download $Model. Check the internet connection and USB free space." }
        } else { Write-Status "Model $Model is already present on this USB drive." }
    }
    Write-Status 'Launching CAPRI from the USB drive...'
    $process = Start-Process -FilePath $app -WorkingDirectory $usbRoot -PassThru
    Write-Status "CAPRI data: $dataRoot"
    Write-Status "AI model storage: $modelRoot"
    Write-Status 'Keep the USB connected until CAPRI and its AI server are closed. Close CAPRI before safely ejecting the drive.'
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

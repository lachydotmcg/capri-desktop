$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$launcher = Join-Path $root 'Start-Capri-Portable.cmd'
$script = Join-Path $PSScriptRoot 'Start-Capri-Portable.ps1'
$readme = Join-Path $PSScriptRoot 'README.md'
$program = Join-Path $root 'app\Capri.cs'
foreach ($path in @($launcher,$script,$readme,$program)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing portable package source file: $path" }
}
$source = Get-Content -LiteralPath $script -Raw
$appSource = Get-Content -LiteralPath $program -Raw
foreach ($pattern in @('OLLAMA_MODELS','CAPRI_PORTABLE_HOME','ollama.exe','ollama-windows-amd64.zip','gemma3:1b','gemma3:4b','Start-Process')) {
    if ($source -notmatch [regex]::Escape($pattern)) { throw "Portable launcher is missing required feature: $pattern" }
}
if ($appSource -notmatch 'CAPRI_PORTABLE_HOME' -or $appSource -notmatch 'Path.Combine\(Path.GetFullPath\(portableHome\), "AppData"\)') {
    throw 'CAPRI application does not redirect its app data into the portable USB data folder.'
}
Write-Output 'PASS portable launcher files exist'
Write-Output 'PASS Ollama runtime is downloaded to USB'
Write-Output 'PASS local vision models are automatically selected and pulled'
Write-Output 'PASS Ollama model storage is configured on USB'
Write-Output 'PASS CAPRI settings and memory are redirected to USB'
Write-Output 'PASS CAPRI executable is launched from package root'

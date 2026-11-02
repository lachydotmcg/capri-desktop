param([string]$App = 'out\call-beta\Capri.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$callApp = [IO.Path]::GetFullPath((Join-Path $root $App))
$callOutput = Join-Path $root 'out\call-mode-check'
New-Item -ItemType Directory -Path $callOutput -Force | Out-Null
$callCheck = Join-Path $callOutput 'CallModeCheck.exe'
$callCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $callCompiler /nologo /target:exe ('/out:' + $callCheck) (Join-Path $PSScriptRoot 'CallModeCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Call mode check compilation failed.' }
& $callCheck $callApp (Join-Path $callOutput 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Call mode checks failed.' }

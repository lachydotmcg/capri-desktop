param([string]$App = 'out\clips\Capri.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$clipApp = [IO.Path]::GetFullPath((Join-Path $root $App))
$clipOutput = Join-Path $root 'out\clip-check'
New-Item -ItemType Directory -Path $clipOutput -Force | Out-Null
$clipCheck = Join-Path $clipOutput 'ClipCheck.exe'
$clipCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $clipCompiler /nologo /target:exe ('/out:' + $clipCheck) (Join-Path $PSScriptRoot 'ClipCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Clip check compilation failed.' }
& $clipCheck $clipApp
if ($LASTEXITCODE -ne 0) { throw 'Clip checks failed.' }

param([string]$App = 'out\clips-r2\Capri.exe', [Parameter(Mandatory = $true)][string]$HiddenRunner)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $HiddenRunner)) { throw 'Supply the existing hidden-desktop runner; this check never runs on the visible desktop.' }
$clipApp = [IO.Path]::GetFullPath((Join-Path $root $App))
$clipOutput = Join-Path $root ('out\clip-panel-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $clipOutput -Force | Out-Null
$clipCheck = Join-Path $clipOutput 'ClipPanelCheck.exe'
$clipCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $clipCompiler /nologo /target:exe /reference:System.Windows.Forms.dll ('/out:' + $clipCheck) (Join-Path $PSScriptRoot 'ClipPanelCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Clip panel check compilation failed.' }
$screen = New-Object System.Threading.Mutex($false, 'Local\CapriScreenTests')
try { [void]$screen.WaitOne() } catch [System.Threading.AbandonedMutexException] { }
$actionsWas = $env:GITHUB_ACTIONS
try {
    $env:GITHUB_ACTIONS = 'true'
    & $HiddenRunner -Exe $clipCheck -Arguments ('"' + $clipApp + '" "' + $clipOutput + '"') -Seconds 300
    $clipResult = Join-Path $clipOutput 'panel-result.txt'
    if (-not (Test-Path -LiteralPath $clipResult)) { throw 'The clip panel check wrote no result.' }
    Get-Content -LiteralPath $clipResult
    'Images: ' + $clipOutput
    if (Select-String -LiteralPath $clipResult -Pattern '^FAIL' -Quiet) { throw 'Clip panel check failed.' }
} finally { $env:GITHUB_ACTIONS = $actionsWas; $screen.ReleaseMutex(); $screen.Dispose(); }

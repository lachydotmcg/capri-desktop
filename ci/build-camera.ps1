param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'out\camera' }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { Write-Warning 'Capri Camera skipped: no Visual Studio C++ tools.'; return $false }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($vs)) { Write-Warning 'Capri Camera skipped: no Visual Studio C++ tools.'; return $false }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$src = Join-Path $root 'native\softcam'
$cpp = @(Get-ChildItem -LiteralPath (Join-Path $src 'baseclasses') -Filter *.cpp) + @(Get-ChildItem -LiteralPath (Join-Path $src 'softcamcore') -Filter *.cpp) + @(Get-ChildItem -LiteralPath (Join-Path $src 'softcam') -Filter *.cpp)
$files = ($cpp | ForEach-Object { '"' + $_.FullName + '"' }) -join ' '
foreach ($arch in @(@{ Name = 'x64'; File = 'CapriCamera64.dll' }, @{ Name = 'x86'; File = 'CapriCamera32.dll' }))
{
    $work = Join-Path $out ('obj-' + $arch.Name)
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $dll = Join-Path $out $arch.File
    $flags = '/nologo /c /O2 /MT /EHsc /std:c++17 /W3 /DWIN32 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /DUNICODE /D_UNICODE'
    $include = '/I"' + $src + '" /I"' + (Join-Path $src 'baseclasses') + '" /I"' + (Join-Path $src 'softcamcore') + '"'
    $link = 'link /nologo /DLL /OUT:"' + $dll + '" /DEF:"' + (Join-Path $src 'softcam\softcam.def') + '" "' + $work + '\*.obj" strmiids.lib winmm.lib kernel32.lib user32.lib gdi32.lib advapi32.lib ole32.lib oleaut32.lib uuid.lib'
    $script = Join-Path $work 'build.cmd'
    Set-Content -LiteralPath $script -Encoding ASCII -Value @(
        '@echo off',
        ('call "' + $vcvars + '" ' + $arch.Name + ' >nul || exit /b 1'),
        ('cd /d "' + $work + '" || exit /b 1'),
        ('cl ' + $flags + ' ' + $include + ' ' + $files + ' || exit /b 1'),
        ($link + ' || exit /b 1'))
    & cmd.exe /c ('"' + $script + '"') | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dll)) { throw ('Capri Camera ' + $arch.Name + ' build failed.') }
}
return $true

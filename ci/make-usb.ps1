param([Parameter(Mandatory = $true)][string]$Destination, [string]$Executable)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $root 'out\windows\Capri.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Capri.exe not found. Run ci/build-windows.ps1 first, or pass -Executable.' }
$target = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath $Executable -Destination (Join-Path $target 'Capri.exe') -Force
$marker = Join-Path $target 'portable.mode'
if (-not (Test-Path -LiteralPath $marker)) { Set-Content -LiteralPath $marker -Value ([guid]::NewGuid().ToString('N')) -Encoding ASCII }
$folders = @('Data\Memory', 'Data\Conversations', 'Data\Settings', 'Data\Knowledge', 'AI\Runtime', 'AI\Models', 'AI\Vision', 'AI\Embeddings', 'Voice', 'Plugins', 'Downloads', 'Logs')
foreach ($folder in $folders) { New-Item -ItemType Directory -Path (Join-Path $target $folder) -Force | Out-Null }
Write-Output "Capri is ready to run from $target"

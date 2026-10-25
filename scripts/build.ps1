$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Check-Exit([string] $step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)" }
}
function Run-WindowsCheck([string] $executable, [string[]] $arguments, [string] $name) {
    $stdout = Join-Path (Resolve-Path out) "$name.stdout.log"
    $stderr = Join-Path (Resolve-Path out) "$name.stderr.log"
    $options = @{ FilePath = $executable; PassThru = $true; RedirectStandardOutput = $stdout; RedirectStandardError = $stderr }
    if ($arguments.Count -gt 0) { $options.ArgumentList = $arguments }
    $process = Start-Process @options
    if (-not $process.WaitForExit(180000)) { $process.Kill(); throw "$name timed out after 180 seconds" }
    $process.WaitForExit()
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0) { throw "$name failed (exit $($process.ExitCode))" }
}
New-Item -ItemType Directory -Force out | Out-Null
dotnet build app/Capri.csproj --configuration Release --output out/app -warnaserror
Check-Exit 'Application build'
dotnet run --project tests/PolicyTests.csproj --configuration Release
Check-Exit 'Portable regression tests'
dotnet run --project tests/PolicyTests.csproj --configuration Release --no-build -- --verify-package out/app/Capri.exe
Check-Exit 'Embedded resource verification'
dotnet build tests/Windows/Capri.WindowsTests.csproj --configuration Release --output out/windows-tests -warnaserror
Check-Exit 'Windows regression runner build'
if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    Run-WindowsCheck (Resolve-Path out/app/Capri.exe) @('--verify-assets') 'assets'
    Run-WindowsCheck (Resolve-Path out/windows-tests/Capri.WindowsTests.exe) @() 'windows-regression'
    # The original smoke runner uses isolated data, fake providers and its own desktop backdrop.
    $smoke = Join-Path (Resolve-Path out) ('smoke-' + [Guid]::NewGuid().ToString('N'))
    Run-WindowsCheck (Resolve-Path out/app/Capri.exe) @('--smoke-test', ('"' + $smoke + '"')) 'legacy-smoke'
    if (-not (Test-Path "$smoke/smoke-test.txt")) { throw 'Smoke runner exited without a fresh result file' }
    Get-Content "$smoke/smoke-test.txt"
} else {
    Write-Host 'NOT RUN: Windows regression executable and native smoke suite require Windows.'
}
$revision = (git rev-parse HEAD).Trim()
Check-Exit 'Git revision lookup'
$trackedChanges = git status --porcelain --untracked-files=no
Check-Exit 'Git working tree check'
if ($trackedChanges) { $revision += '-dirty' }
dotnet run --project tests/PolicyTests.csproj --configuration Release --no-build -- --package out/app out/artifacts $revision
Check-Exit 'ZIP packaging and extraction validation'

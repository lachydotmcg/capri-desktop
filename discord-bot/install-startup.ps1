$ErrorActionPreference = 'Stop'
$node = (Get-Command node.exe -ErrorAction Stop).Source.Replace("'", "''")
$script = (Join-Path $PSScriptRoot 'capri-bot.mjs').Replace("'", "''")
$directory = $PSScriptRoot.Replace("'", "''")
$command = "`$process = Start-Process -FilePath '$node' -ArgumentList '`"$script`"' -WorkingDirectory '$directory' -WindowStyle Hidden -Wait -PassThru; exit `$process.ExitCode"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
$shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $shell -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand $encoded" -WorkingDirectory $PSScriptRoot
$account = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $account
$principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'Capri Discord Bot' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host 'Capri will start hidden when you log in.'

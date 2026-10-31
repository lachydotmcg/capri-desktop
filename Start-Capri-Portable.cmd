@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0portable\Start-Capri-Portable.ps1" %*
if errorlevel 1 pause
endlocal

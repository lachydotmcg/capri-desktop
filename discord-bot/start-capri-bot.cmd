@echo off
cd /d "%~dp0"
node "%~dp0capri-bot.mjs"
if errorlevel 1 pause

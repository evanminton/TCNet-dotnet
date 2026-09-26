@echo off
rem Builds the standalone Windows install of TCNet Monitor (portable zip + Setup.exe).
rem Output: artifacts\installer\   Log: installer-log.txt
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File installer\build-installer.ps1 %* > installer-log.txt 2>&1
echo INSTALLER EXIT %ERRORLEVEL% >> installer-log.txt
if exist artifacts\installer start "" explorer artifacts\installer

@echo off
rem Double-click: builds Debug and Release, runs the tests and builds the MAUI app for this PC.
rem Everything is written to build-log.txt next to this file.
cd /d "%~dp0"
echo == %date% %time% == > build-log.txt
git log --oneline -1 >> build-log.txt 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -c Both -App >> build-log.txt 2>&1
echo EXIT %ERRORLEVEL% >> build-log.txt

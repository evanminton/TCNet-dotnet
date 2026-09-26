@echo off
rem Creates the git repo on first run, builds Debug and Release, runs tests, builds the MAUI app for this PC.
rem Log: build-log.txt
cd /d "%~dp0"
if not exist .git (
  git init -b main > build-log.txt 2>&1
  git add -A >> build-log.txt 2>&1
  git commit -q -m "TCNet V3.5.1B library, tests, tcnet-monitor CLI and MAUI app - fresh build from the PDF" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01JHHJ5KvtHX8aqKq9sjtzpD" >> build-log.txt 2>&1
  git log --oneline -1 >> build-log.txt 2>&1
) else (
  echo repo exists > build-log.txt
)
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -c Both >> build-log.txt 2>&1
echo LIBRARY EXIT %ERRORLEVEL% >> build-log.txt
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -c Both -App -NoTest >> build-log.txt 2>&1
echo APP EXIT %ERRORLEVEL% >> build-log.txt

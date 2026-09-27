@echo off
rem Usage: push-github "commit message"
rem Commits all changes with that message (skipped when no message is given) and pushes main to github.com/evanminton/TCNet-dotnet. Log: build-log.txt
cd /d "%~dp0"
echo == status == > build-log.txt
git status --short --branch >> build-log.txt 2>&1
git remote get-url origin >nul 2>&1 || git remote add origin https://github.com/evanminton/TCNet-dotnet.git
echo == commit == >> build-log.txt
if "%~1"=="" (
  echo no commit message given, pushing existing commits only >> build-log.txt
) else (
  git add -A >> build-log.txt 2>&1
  git diff --cached --quiet && (echo nothing to commit >> build-log.txt) || git commit -q -m "%~1" >> build-log.txt 2>&1
)
echo == pull == >> build-log.txt
git pull --rebase origin main >> build-log.txt 2>&1
echo == push == >> build-log.txt
git push -u origin main >> build-log.txt 2>&1
echo PUSH EXIT %ERRORLEVEL% >> build-log.txt
git log --oneline -3 >> build-log.txt 2>&1
git status --short --branch >> build-log.txt 2>&1

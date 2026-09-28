@echo off
setlocal EnableExtensions
title MIT Asset Agent Installer
cd /d "%~dp0"

echo.
echo  MIT Asset Agent — installer
echo  Do NOT double-click MITAssetAgent.exe
echo.

REM Elevate if not already admin
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting Administrator permission...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

if not exist "%~dp0Install-MITAssetAgent.ps1" (
  echo ERROR: Install-MITAssetAgent.ps1 not found next to this file.
  echo Extract the FULL zip, then run Install.cmd from that folder.
  pause
  exit /b 1
)

if not exist "%~dp0MITAssetAgent.exe" (
  echo ERROR: MITAssetAgent.exe not found in this folder.
  echo You may have extracted only part of the zip, or opened the wrong folder.
  pause
  exit /b 1
)

if not exist "%~dp0install-config.json" (
  echo.
  echo First-time setup: creating install-config.json ...
  echo.
  if exist "%~dp0install-config.example.json" (
    copy /Y "%~dp0install-config.example.json" "%~dp0install-config.json" >nul
  ) else (
    echo ERROR: install-config.example.json is missing.
    pause
    exit /b 1
  )
  echo Edit EnrollmentKey in Notepad, SAVE, close Notepad, then run Install.cmd again.
  echo.
  notepad "%~dp0install-config.json"
  echo.
  echo Config saved. Run Install.cmd again to install.
  pause
  exit /b 0
)

REM Catch common mistake: placeholder enrollment key still in the file
findstr /I /C:"YOUR_AGENT_ENROLLMENT_KEY" "%~dp0install-config.json" >nul
if not errorlevel 1 (
  echo.
  echo ERROR: install-config.json still has the placeholder EnrollmentKey.
  echo Replace YOUR_AGENT_ENROLLMENT_KEY with your real enrollment key, save, then re-run.
  echo.
  notepad "%~dp0install-config.json"
  pause
  exit /b 1
)

findstr /I /C:"YOUR_PROJECT" "%~dp0install-config.json" >nul
if not errorlevel 1 (
  echo.
  echo ERROR: install-config.json still has placeholder SupabaseUrl YOUR_PROJECT.
  echo Set the real https://....supabase.co URL, save, then re-run.
  echo.
  notepad "%~dp0install-config.json"
  pause
  exit /b 1
)

set "LOG=%~dp0install-log.txt"
echo Running installer... (log: install-log.txt)
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MITAssetAgent.ps1" -PublishDir "%~dp0" -ConfigFile "%~dp0install-config.json" > "%LOG%" 2>&1
set ERR=%ERRORLEVEL%

type "%LOG%"
echo.
if %ERR% neq 0 (
  echo Install failed with exit code %ERR%.
  echo Full details are in: %LOG%
  echo If the window closed early, open install-log.txt in this folder.
) else (
  echo Done. Check Services for "MIT Asset Agent" and MIT Asset -^> Agents.
)
pause
exit /b %ERR%

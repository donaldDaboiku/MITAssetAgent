@echo off
setlocal
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
  echo Extract the full zip first, then run Install.cmd from that folder.
  pause
  exit /b 1
)

if not exist "%~dp0install-config.json" (
  echo.
  echo No install-config.json found.
  echo.
  echo 1. Copy install-config.example.json to install-config.json
  echo 2. Edit install-config.json with your Supabase URL and enrollment key
  echo 3. Run Install.cmd again
  echo.
  if exist "%~dp0install-config.example.json" (
    copy /Y "%~dp0install-config.example.json" "%~dp0install-config.json" >nul
    echo Created install-config.json from the example — edit it now, then re-run Install.cmd.
    notepad "%~dp0install-config.json"
  )
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MITAssetAgent.ps1" -PublishDir "%~dp0." -ConfigFile "%~dp0install-config.json"
set ERR=%ERRORLEVEL%
echo.
if %ERR% neq 0 (
  echo Install failed with exit code %ERR%.
) else (
  echo Done. Check Services for "MIT Asset Agent" and MIT Asset -^> Agents.
)
pause
exit /b %ERR%

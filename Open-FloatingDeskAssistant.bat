@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\launch-tool.ps1" -Mode release
if errorlevel 1 (
  echo.
  echo Launch failed. Press any key to exit.
  pause >nul
  exit /b 1
)

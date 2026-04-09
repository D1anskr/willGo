@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\install-and-setup.ps1"
if errorlevel 1 (
  echo.
  echo Install failed. Press any key to exit.
  pause >nul
  exit /b 1
)
echo.
echo Install finished. Press any key to exit.
pause >nul

@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\build-setup-exe.ps1"
if errorlevel 1 (
  echo.
  echo Setup build failed. Press any key to exit.
  pause >nul
  exit /b 1
)
echo.
echo Setup build finished. Press any key to exit.
pause >nul
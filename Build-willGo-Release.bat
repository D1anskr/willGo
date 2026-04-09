@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\build-release.ps1"
if errorlevel 1 (
  echo.
  echo Release build failed. Press any key to exit.
  pause >nul
  exit /b 1
)
echo.
echo Release build finished. Press any key to exit.
pause >nul

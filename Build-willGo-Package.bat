@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\build-distribution.ps1"
if errorlevel 1 (
  echo.
  echo Package build failed. Press any key to exit.
  pause >nul
  exit /b 1
)
echo.
echo Package build finished. Press any key to exit.
pause >nul
@echo off
setlocal

echo Repair install will clean build artifacts and reinstall the app.
set REMOVE_DATA_ARG=
set /p REMOVE_DATA=Also remove local config/logs? (y/N): 
if /I "%REMOVE_DATA%"=="y" set REMOVE_DATA_ARG=-RemoveUserData
if /I "%REMOVE_DATA%"=="yes" set REMOVE_DATA_ARG=-RemoveUserData

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\repair-install.ps1" %REMOVE_DATA_ARG%
if errorlevel 1 (
  echo.
  echo Repair failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo Repair finished. Press any key to exit.
pause >nul

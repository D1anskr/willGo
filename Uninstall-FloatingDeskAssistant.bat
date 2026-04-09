@echo off
setlocal

set REMOVE_DATA_ARG=
set /p REMOVE_DATA=Remove local config/logs too? (y/N): 
if /I "%REMOVE_DATA%"=="y" set REMOVE_DATA_ARG=-RemoveUserData
if /I "%REMOVE_DATA%"=="yes" set REMOVE_DATA_ARG=-RemoveUserData

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\windows\uninstall.ps1" %REMOVE_DATA_ARG%
if errorlevel 1 (
  echo.
  echo Uninstall failed. Press any key to exit.
  pause >nul
  exit /b 1
)

echo.
echo Uninstall finished. Press any key to exit.
pause >nul

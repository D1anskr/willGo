# Quick Start (Windows)

## Recommended: build a release for sharing
Double-click:

- `Build-willGo-Release.bat`

What it does:
- Automatically bumps the app version (`patch` by default, for example `1.0.0 -> 1.0.1`)
- Builds a fresh package folder, zip, and single-file `Setup.exe`
- Makes the output filename change with each release, so it is harder to send an old installer by mistake

Output:
- `dist\willGo-<version>-win-x64\`
- `dist\willGo-<version>-win-x64.zip`
- `dist\willGo-Setup-<version>-win-x64.exe`

## Build only a package folder + zip
Double-click:

- `Build-willGo-Package.bat`

What it does:
- Generates `willGo` branding assets
- Publishes a self-contained Windows x64 app
- Creates a shareable package folder and zip under `dist\`
- Keeps the current project version unchanged

## Build only a single-file setup for the current version
Double-click:

- `Build-willGo-Setup.bat`

What it does:
- Rebuilds the latest package and wraps it as a single-file `Setup.exe`
- Keeps the current project version unchanged
- Useful for same-version rebuilds or local verification

## Install on another computer
If you send the zip package, extract it and double-click:

- `Install-willGo.bat`

If you send the single file installer, the other computer can double-click:

- `dist\willGo-Setup-<version>-win-x64.exe`

Install behavior:
- Installs the app to `%LOCALAPPDATA%\Programs\willGo`
- Creates desktop shortcut: `willGo`
- Creates Start menu shortcuts
- Registers an uninstall entry in Windows Installed apps

## One-click install from source
Double-click:

- `Install-FloatingDeskAssistant.bat`

What it does:
- Installs .NET 8 SDK automatically (via winget, only if missing)
- Restores, builds, and publishes the app
- Creates desktop shortcut: `Floating Desk Assistant`

## One-click open
After source install, double-click either:

- Desktop shortcut: `Floating Desk Assistant`
- `Open-FloatingDeskAssistant.bat`

## One-click repair install
Double-click:

- `Repair-FloatingDeskAssistant.bat`

What it does:
- Stops running app process
- Cleans broken build artifacts and shortcut
- Re-runs install/publish flow
- Recreates desktop shortcut
- Optional: remove local config/logs

## One-click uninstall
Double-click:

- `Uninstall-FloatingDeskAssistant.bat`

What it does:
- Stops running `FloatingDeskAssistant` process
- Removes desktop shortcut
- Removes build/publish artifacts (`bin/`, `obj/`)
- Optionally removes local data (`%LOCALAPPDATA%\FloatingDeskAssistant`)

## Optional command line
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1 -Bump Minor
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1 -Version 1.2.0
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\install-and-setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\launch-tool.ps1 -Mode release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\repair-install.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\uninstall.ps1 -RemoveUserData
```

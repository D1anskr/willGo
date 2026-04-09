# willGo Windows Distribution

## Recommended release flow
For a real shareable release, use:

- `Build-willGo-Release.bat`

Or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1
```

What it does:
- Automatically increments the project version (`patch` by default)
- Builds a fresh package folder and zip
- Builds a fresh single-file `Setup.exe`
- Avoids reusing the same installer filename across releases

Output:
- `dist\willGo-<version>-win-x64\`
- `dist\willGo-<version>-win-x64.zip`
- `dist\willGo-Setup-<version>-win-x64.exe`

## Version control options
Default behavior bumps the `patch` version:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1
```

Bump `minor` instead:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1 -Bump Minor
```

Set an explicit version:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-release.ps1 -Version 1.2.0
```

## Lower-level build options
Build only the package folder and zip:

- `Build-willGo-Package.bat`

Or:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
```

Build only a single-file setup for the current version:

- `Build-willGo-Setup.bat`

Or:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
```

## What to send to another computer
You can share either:
- `dist\willGo-Setup-<version>-win-x64.exe`
- `dist\willGo-<version>-win-x64.zip`

If you send the zip, the other computer should extract it and run:
- `Install-willGo.bat`

If you send the setup executable, the other computer can simply double-click it.

## Install behavior
The installed app:
- Goes to `%LOCALAPPDATA%\Programs\willGo`
- Creates a desktop shortcut named `willGo`
- Creates Start menu entries
- Registers an uninstall entry in Windows Installed apps

## Notes
- `build-release.ps1` updates `src\FloatingDeskAssistant\FloatingDeskAssistant.csproj` with the new version only after the release build starts; if the build fails, it restores the previous version automatically.
- `build-setup-exe.ps1` still rebuilds fresh package contents before wrapping the `Setup.exe`; use it when you want the latest installer without changing the version number.
- For shipping to other machines, prefer the release flow so the versioned filenames clearly show which installer is newest.

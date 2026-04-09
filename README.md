# willGo

`willGo` is a Windows desktop assistant built with `.NET 8`, `WPF`, and `Windows Forms` interop.
It supports screenshot-based sending, local image attachment, configurable model endpoints, and a phone-accessible LAN trigger page for remote desktop capture.

## Requirements

- Windows 10/11
- `.NET 8 SDK` for local build from source
- Network access for first-time NuGet restore
- `IExpress.exe` if you want to generate the single-file `Setup.exe`

## Repository Layout

```text
src/FloatingDeskAssistant/   Main application source
scripts/windows/            Build, package, install, repair, and uninstall scripts
docs/                       Supporting release and planning notes
QUICKSTART-WINDOWS.md       Windows usage and build shortcuts
```

## Clone

SSH is the most reliable option if you plan to push changes:

```powershell
git clone git@github.com:D1anskr/willGo.git
cd willGo
```

HTTPS clone also works for read-only or environments without SSH:

```powershell
git clone https://github.com/D1anskr/willGo.git
cd willGo
```

## Build From Source

Build the app directly:

```powershell
dotnet build .\src\FloatingDeskAssistant\FloatingDeskAssistant.csproj -c Release
```

Run the packaging flow:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
```

Build the single-file installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
```

Convenience batch files are available in the repository root:

- `Build-willGo-Release.bat`
- `Build-willGo-Package.bat`
- `Build-willGo-Setup.bat`
- `Install-FloatingDeskAssistant.bat`
- `Repair-FloatingDeskAssistant.bat`
- `Uninstall-FloatingDeskAssistant.bat`

See [`QUICKSTART-WINDOWS.md`](./QUICKSTART-WINDOWS.md) for the Windows-oriented workflow and output paths.

## Release Output

The release scripts generate versioned artifacts under `dist/`:

- `dist\willGo-<version>-win-x64\`
- `dist\willGo-<version>-win-x64.zip`
- `dist\willGo-Setup-<version>-win-x64.exe`

These artifacts are intentionally not tracked in git.

## Daily Git Workflow

Sync before starting work:

```powershell
git pull --rebase origin main
```

After editing files:

```powershell
git status
git add .
git commit -m "Describe your change"
git push origin main
```

Useful notes:

- `dist/`, `bin/`, and `obj/` are ignored because they are rebuildable outputs.
- `build-release.ps1` updates `src/FloatingDeskAssistant/FloatingDeskAssistant.csproj` with a new version when you create a release. If you want that version bump to remain in source control, commit it.
- If another machine cannot push over HTTPS, switch the remote to SSH:

```powershell
git remote set-url origin git@github.com:D1anskr/willGo.git
```

## Verification

This repository was verified from a fresh clone with:

```powershell
dotnet build .\src\FloatingDeskAssistant\FloatingDeskAssistant.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-distribution.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\build-setup-exe.ps1
```

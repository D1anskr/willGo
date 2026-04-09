param(
    [switch]$NoDesktopShortcut = $false,
    [switch]$NoStartMenuShortcut = $false,
    [switch]$NoLaunch = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Get-PackageRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Get-PackageMetadata {
    param([string]$PackageRoot)

    $metadataPath = Join-Path $PackageRoot 'package-info.json'
    if (-not (Test-Path $metadataPath)) {
        throw "Package metadata not found: $metadataPath"
    }

    return (Get-Content $metadataPath -Raw | ConvertFrom-Json)
}

function Get-ProcessNames {
    param($Metadata)

    $names = New-Object System.Collections.Generic.List[string]
    if ($Metadata.ProcessNames) {
        foreach ($item in $Metadata.ProcessNames) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item) -and -not $names.Contains([string]$item)) {
                $names.Add([string]$item)
            }
        }
    }

    $exeStem = [System.IO.Path]::GetFileNameWithoutExtension([string]$Metadata.ExecutableName)
    if (-not [string]::IsNullOrWhiteSpace($exeStem) -and -not $names.Contains($exeStem)) {
        $names.Add($exeStem)
    }

    return $names.ToArray()
}

function Stop-AppProcesses {
    param([string[]]$ProcessNames)

    foreach ($processName in $ProcessNames) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                Stop-Process -Id $_.Id -Force
                Write-Host "Stopped process: $($_.ProcessName) [$($_.Id)]" -ForegroundColor Yellow
            }
            catch {
                Write-Warning "Failed to stop $($_.ProcessName) [$($_.Id)]: $($_.Exception.Message)"
            }
        }
    }
}

function Reset-Directory {
    param(
        [string]$Path,
        [string]$Label
    )

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Write-Host "$Label ready: $Path" -ForegroundColor DarkGray
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination,
        [string]$Label
    )

    if (-not (Test-Path $Source)) {
        throw "Missing required directory: $Source"
    }

    Reset-Directory -Path $Destination -Label $Label
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description,
        [string]$IconLocation,
        [string]$Arguments = ''
    )

    $shortcutDir = Split-Path -Parent $ShortcutPath
    if ($shortcutDir -and -not (Test-Path $shortcutDir)) {
        New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
    }

    $wshShell = New-Object -ComObject WScript.Shell
    $shortcut = $wshShell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.WindowStyle = 1

    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        $shortcut.Arguments = $Arguments
    }

    if (-not [string]::IsNullOrWhiteSpace($IconLocation)) {
        $shortcut.IconLocation = $IconLocation
    }

    $shortcut.Save()
}

function Register-UninstallEntry {
    param(
        $Metadata,
        [string]$InstallRoot,
        [string]$ExePath,
        [string]$UninstallScriptPath
    )

    $keyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($Metadata.UninstallKeyName)"
    $estimatedSizeKb = [int](
        (Get-ChildItem -Path $InstallRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB
    )
    $uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$UninstallScriptPath`""
    $quietUninstallCommand = "$uninstallCommand -Quiet"

    New-Item -Path $keyPath -Force | Out-Null
    Set-ItemProperty -Path $keyPath -Name 'DisplayName' -Value ([string]$Metadata.AppDisplayName)
    Set-ItemProperty -Path $keyPath -Name 'DisplayVersion' -Value ([string]$Metadata.Version)
    Set-ItemProperty -Path $keyPath -Name 'Publisher' -Value ([string]$Metadata.Publisher)
    Set-ItemProperty -Path $keyPath -Name 'InstallLocation' -Value $InstallRoot
    Set-ItemProperty -Path $keyPath -Name 'DisplayIcon' -Value "$ExePath,0"
    Set-ItemProperty -Path $keyPath -Name 'UninstallString' -Value $uninstallCommand
    Set-ItemProperty -Path $keyPath -Name 'QuietUninstallString' -Value $quietUninstallCommand
    Set-ItemProperty -Path $keyPath -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $keyPath -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty -Path $keyPath -Name 'InstallDate' -Value (Get-Date -Format 'yyyyMMdd')
    Set-ItemProperty -Path $keyPath -Name 'EstimatedSize' -Value $estimatedSizeKb -Type DWord
}

$packageRoot = Get-PackageRoot
$metadata = Get-PackageMetadata -PackageRoot $packageRoot
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\$($metadata.InstallFolderName)"
$appInstallDir = Join-Path $installRoot 'app'
$toolsInstallDir = Join-Path $installRoot 'tools'
$assetsInstallDir = Join-Path $installRoot 'assets'
$appSourceDir = Join-Path $packageRoot 'app'
$toolsSourceDir = Join-Path $packageRoot 'tools'
$assetsSourceDir = Join-Path $packageRoot 'assets'
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "$($metadata.ShortcutName).lnk"
$startMenuDir = Join-Path ([Environment]::GetFolderPath('Programs')) $metadata.StartMenuFolderName
$startMenuShortcutPath = Join-Path $startMenuDir "$($metadata.ShortcutName).lnk"
$startMenuUninstallPath = Join-Path $startMenuDir "Uninstall $($metadata.ShortcutName).lnk"

Write-Step 'Preparing install directories'
Stop-AppProcesses -ProcessNames (Get-ProcessNames -Metadata $metadata)
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-DirectoryContents -Source $appSourceDir -Destination $appInstallDir -Label 'Application directory'
Copy-DirectoryContents -Source $toolsSourceDir -Destination $toolsInstallDir -Label 'Tools directory'
Copy-DirectoryContents -Source $assetsSourceDir -Destination $assetsInstallDir -Label 'Assets directory'
Copy-Item -Path (Join-Path $packageRoot 'package-info.json') -Destination (Join-Path $installRoot 'package-info.json') -Force

$exePath = Join-Path $appInstallDir ([string]$metadata.ExecutableName)
$iconPath = Join-Path $assetsInstallDir ([string]$metadata.IconFileName)
$uninstallScriptPath = Join-Path $toolsInstallDir 'uninstall.ps1'

if (-not (Test-Path $exePath)) {
    throw "Installed executable not found: $exePath"
}

Write-Step 'Creating shortcuts'
if (-not $NoDesktopShortcut) {
    New-Shortcut -ShortcutPath $desktopShortcutPath -TargetPath $exePath -WorkingDirectory $appInstallDir -Description ([string]$metadata.AppDisplayName) -IconLocation "$exePath,0"
    Write-Host "Desktop shortcut created: $desktopShortcutPath" -ForegroundColor Green
}

if (-not $NoStartMenuShortcut) {
    New-Shortcut -ShortcutPath $startMenuShortcutPath -TargetPath $exePath -WorkingDirectory $appInstallDir -Description ([string]$metadata.AppDisplayName) -IconLocation "$exePath,0"
    New-Shortcut -ShortcutPath $startMenuUninstallPath -TargetPath 'powershell.exe' -WorkingDirectory $toolsInstallDir -Description "Uninstall $($metadata.AppDisplayName)" -IconLocation "$exePath,0" -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$uninstallScriptPath`""
    Write-Host "Start menu shortcuts created: $startMenuDir" -ForegroundColor Green
}

Write-Step 'Registering uninstall entry'
Register-UninstallEntry -Metadata $metadata -InstallRoot $installRoot -ExePath $exePath -UninstallScriptPath $uninstallScriptPath

Write-Step 'Completed'
Write-Host "Installed to: $installRoot" -ForegroundColor Green
Write-Host "You can now launch $($metadata.AppDisplayName) from the desktop or Start menu." -ForegroundColor Green

if (-not $NoLaunch) {
    Start-Process -FilePath $exePath -WorkingDirectory $appInstallDir
    Write-Host 'Application launched.' -ForegroundColor Yellow
}

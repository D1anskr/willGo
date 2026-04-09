param(
    [switch]$RemoveUserData = $false,
    [switch]$Quiet = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Get-InstallRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Get-PackageMetadata {
    param([string]$InstallRoot)

    $metadataPath = Join-Path $InstallRoot 'package-info.json'
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

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force
        Write-Host "Removed: $Path" -ForegroundColor Green
    }
}

$installRoot = Get-InstallRoot
$metadata = Get-PackageMetadata -InstallRoot $installRoot
$appDisplayName = [string]$metadata.AppDisplayName
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "$($metadata.ShortcutName).lnk"
$startMenuDir = Join-Path ([Environment]::GetFolderPath('Programs')) $metadata.StartMenuFolderName
$uninstallKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($metadata.UninstallKeyName)"
$userDataPath = Join-Path $env:LOCALAPPDATA ([string]$metadata.UserDataFolderName)

if (-not $Quiet) {
    Write-Host "$appDisplayName will be removed from this user profile." -ForegroundColor Yellow
    if ($RemoveUserData) {
        Write-Host "User data will also be removed: $userDataPath" -ForegroundColor Yellow
    }

    $confirmation = Read-Host 'Continue uninstall? (y/N)'
    if ($confirmation -notin @('y', 'Y', 'yes', 'YES')) {
        Write-Host 'Canceled.' -ForegroundColor DarkGray
        exit 0
    }
}

Write-Step 'Stopping application'
Stop-AppProcesses -ProcessNames (Get-ProcessNames -Metadata $metadata)

Write-Step 'Removing shortcuts'
Remove-IfExists -Path $desktopShortcutPath
Remove-IfExists -Path $startMenuDir

Write-Step 'Removing uninstall entry'
if (Test-Path $uninstallKeyPath) {
    Remove-Item -Path $uninstallKeyPath -Recurse -Force
    Write-Host "Removed registry key: $uninstallKeyPath" -ForegroundColor Green
}

if ($RemoveUserData) {
    Write-Step 'Removing user data'
    Remove-IfExists -Path $userDataPath
}

Write-Step 'Removing installed files'
$cleanupCommand = "ping 127.0.0.1 -n 3 >nul && rmdir /s /q `"$installRoot`""
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cleanupCommand -WindowStyle Hidden
Write-Host "Scheduled directory removal: $installRoot" -ForegroundColor Green

Write-Step 'Completed'
Write-Host "$appDisplayName uninstall completed." -ForegroundColor Green
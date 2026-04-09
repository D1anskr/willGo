param(
    [switch]$RemoveUserData = $false,
    [switch]$Force = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Stop-AppProcess {
    $proc = Get-Process -Name "FloatingDeskAssistant" -ErrorAction SilentlyContinue
    if (-not $proc) {
        return
    }

    Write-Step "Stopping running process"
    foreach ($p in $proc) {
        try {
            Stop-Process -Id $p.Id -Force
            Write-Host "Stopped PID $($p.Id)" -ForegroundColor Yellow
        }
        catch {
            Write-Warning "Failed to stop PID $($p.Id): $($_.Exception.Message)"
        }
    }
}

function Remove-DesktopShortcut {
    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopPath "Floating Desk Assistant.lnk"

    Write-Step "Removing desktop shortcut"
    if (Test-Path $shortcutPath) {
        Remove-Item -Path $shortcutPath -Force
        Write-Host "Removed: $shortcutPath" -ForegroundColor Green
    }
    else {
        Write-Host "Shortcut not found, skipped." -ForegroundColor DarkGray
    }
}

function Remove-PublishOutput {
    param([string]$RepoRoot)

    $publishRoot = Join-Path $RepoRoot "src/FloatingDeskAssistant/bin"
    $objRoot = Join-Path $RepoRoot "src/FloatingDeskAssistant/obj"

    Write-Step "Removing build outputs"
    if (Test-Path $publishRoot) {
        Remove-Item -Path $publishRoot -Recurse -Force
        Write-Host "Removed: $publishRoot" -ForegroundColor Green
    }
    else {
        Write-Host "No bin directory found, skipped." -ForegroundColor DarkGray
    }

    if (Test-Path $objRoot) {
        Remove-Item -Path $objRoot -Recurse -Force
        Write-Host "Removed: $objRoot" -ForegroundColor Green
    }
    else {
        Write-Host "No obj directory found, skipped." -ForegroundColor DarkGray
    }
}

function Remove-UserData {
    $appDataRoot = Join-Path $env:LOCALAPPDATA "FloatingDeskAssistant"

    Write-Step "Removing user data"
    if (Test-Path $appDataRoot) {
        Remove-Item -Path $appDataRoot -Recurse -Force
        Write-Host "Removed: $appDataRoot" -ForegroundColor Green
    }
    else {
        Write-Host "No user data found, skipped." -ForegroundColor DarkGray
    }
}

function Test-IsWindows {
    if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
        return [bool](Get-Variable -Name IsWindows -ValueOnly)
    }

    return ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)
}

if (-not (Test-IsWindows)) {
    throw "This script only runs on Windows."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path

if (-not $Force) {
    Write-Host "This will remove published binaries and desktop shortcut." -ForegroundColor Yellow
    if ($RemoveUserData) {
        Write-Host "User data (%LOCALAPPDATA%\\FloatingDeskAssistant) will also be removed." -ForegroundColor Yellow
    }

    $confirm = Read-Host "Continue uninstall? (y/N)"
    if ($confirm -notin @("y", "Y", "yes", "YES")) {
        Write-Host "Canceled." -ForegroundColor DarkGray
        exit 0
    }
}

Stop-AppProcess
Remove-DesktopShortcut
Remove-PublishOutput -RepoRoot $repoRoot

if ($RemoveUserData) {
    Remove-UserData
}

Write-Step "Completed"
Write-Host "Uninstall completed." -ForegroundColor Green

param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsWindows {
    if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
        return [bool](Get-Variable -Name IsWindows -ValueOnly)
    }

    return ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)
}

if (-not (Test-IsWindows)) {
    throw "This script only runs on Windows."
}

$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "Floating Desk Assistant.lnk"
$launcherBat = Join-Path $RepoRoot "Open-FloatingDeskAssistant.bat"

if (-not (Test-Path $launcherBat)) {
    throw "Launcher batch file not found: $launcherBat"
}

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $launcherBat
$shortcut.WorkingDirectory = $RepoRoot
$shortcut.WindowStyle = 1
$shortcut.Description = "Open Floating Desk Assistant"

$iconCandidate = Join-Path $RepoRoot "src/FloatingDeskAssistant/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/FloatingDeskAssistant.exe"
if (Test-Path $iconCandidate) {
    $shortcut.IconLocation = "$iconCandidate,0"
}

$shortcut.Save()
Write-Host "Desktop shortcut created: $shortcutPath" -ForegroundColor Green

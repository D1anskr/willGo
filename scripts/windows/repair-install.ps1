param(
    [switch]$RemoveUserData = $false,
    [switch]$SelfContained = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
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
$uninstallScript = Join-Path $repoRoot "scripts/windows/uninstall.ps1"
$installScript = Join-Path $repoRoot "scripts/windows/install-and-setup.ps1"

if (-not (Test-Path $uninstallScript)) {
    throw "Uninstall script not found: $uninstallScript"
}

if (-not (Test-Path $installScript)) {
    throw "Install script not found: $installScript"
}

Write-Step "Repair phase 1/2: clean broken artifacts"
$uninstallArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $uninstallScript,
    "-Force"
)
if ($RemoveUserData) {
    $uninstallArgs += "-RemoveUserData"
}

& powershell @uninstallArgs
if ($LASTEXITCODE -ne 0) {
    throw "Uninstall phase failed with exit code $LASTEXITCODE"
}

Write-Step "Repair phase 2/2: reinstall and republish"
$installArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $installScript,
    "-CreateDesktopShortcut"
)
if ($SelfContained) {
    $installArgs += "-SelfContained"
}

& powershell @installArgs
if ($LASTEXITCODE -ne 0) {
    throw "Install phase failed with exit code $LASTEXITCODE"
}

Write-Step "Repair completed"
Write-Host "Repair install finished successfully." -ForegroundColor Green
Write-Host "You can now launch: Open-FloatingDeskAssistant.bat" -ForegroundColor Yellow

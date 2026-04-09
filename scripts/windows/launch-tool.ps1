param(
    [ValidateSet("debug", "release")]
    [string]$Mode = "release"
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
    throw "This launcher only supports Windows."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projectPath = Join-Path $repoRoot "src/FloatingDeskAssistant/FloatingDeskAssistant.csproj"

$publishedExe = Join-Path $repoRoot "src/FloatingDeskAssistant/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/FloatingDeskAssistant.exe"

if ($Mode -eq "release" -and (Test-Path $publishedExe)) {
    Start-Process -FilePath $publishedExe
    return
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    throw "dotnet not found. Please run Install-FloatingDeskAssistant.bat first."
}

if ($Mode -eq "debug") {
    dotnet run --project $projectPath -c Debug
}
else {
    dotnet run --project $projectPath -c Release
}

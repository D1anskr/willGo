param(
    [switch]$SelfContained = $true,
    [switch]$CreateDesktopShortcut = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Assert-Windows {
    $runningOnWindows = $false

    if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
        $runningOnWindows = [bool](Get-Variable -Name IsWindows -ValueOnly)
    }
    else {
        $runningOnWindows = ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)
    }

    if (-not $runningOnWindows) {
        throw "This script only runs on Windows."
    }
}

function Ensure-DotnetSdk {
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCmd) {
        Write-Host "Found dotnet: $($dotnetCmd.Source)" -ForegroundColor Green
        return
    }

    $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
    if ($wingetCmd) {
        Write-Step "Installing .NET 8 SDK via winget"
        winget install --id Microsoft.DotNet.SDK.8 --exact --source winget --accept-package-agreements --accept-source-agreements
    }
    else {
        Write-Step "Installing .NET 8 SDK via dotnet-install script"

        $dotnetInstallScript = Join-Path $env:TEMP "dotnet-install.ps1"
        $dotnetInstallUri = "https://dot.net/v1/dotnet-install.ps1"
        $installDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"

        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        }
        catch {
            Write-Host "Unable to enforce TLS1.2 explicitly, continuing." -ForegroundColor DarkGray
        }

        Invoke-WebRequest -Uri $dotnetInstallUri -OutFile $dotnetInstallScript
        & powershell -NoProfile -ExecutionPolicy Bypass -File $dotnetInstallScript -Channel "8.0" -InstallDir $installDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-install script failed with exit code $LASTEXITCODE"
        }

        if (Test-Path $installDir) {
            $env:PATH = "$installDir;$env:PATH"
        }
    }

    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        throw "dotnet installation did not complete successfully."
    }

    Write-Host "dotnet installed: $($dotnetCmd.Source)" -ForegroundColor Green
}

function Build-And-Publish {
    param(
        [string]$RepoRoot,
        [bool]$PublishSelfContained
    )

    $projectPath = Join-Path $RepoRoot "src/FloatingDeskAssistant/FloatingDeskAssistant.csproj"
    if (-not (Test-Path $projectPath)) {
        throw "Project file not found: $projectPath"
    }

    Write-Step "Restoring dependencies"
    dotnet restore $projectPath
    if ((-not $?) -or $LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE"
    }

    Write-Step "Building Debug"
    dotnet build $projectPath -c Debug
    if ((-not $?) -or $LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    Write-Step "Publishing Release"
    if ($PublishSelfContained) {
        dotnet publish $projectPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
    }
    else {
        dotnet publish $projectPath -c Release -r win-x64 --self-contained false
    }

    if ((-not $?) -or $LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

function Ensure-DesktopShortcut {
    param([string]$RepoRoot)

    $shortcutScript = Join-Path $RepoRoot "scripts/windows/create-desktop-shortcut.ps1"
    if (-not (Test-Path $shortcutScript)) {
        throw "Shortcut script not found: $shortcutScript"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $shortcutScript -RepoRoot $RepoRoot
}

Assert-Windows
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path

Write-Step "Checking prerequisites"
Ensure-DotnetSdk

Write-Step "Compiling application"
Build-And-Publish -RepoRoot $repoRoot -PublishSelfContained:$SelfContained

if ($CreateDesktopShortcut) {
    Write-Step "Creating desktop shortcut"
    Ensure-DesktopShortcut -RepoRoot $repoRoot
}

Write-Step "Completed"
Write-Host "Environment setup and build completed." -ForegroundColor Green
Write-Host "You can now double-click: Open-FloatingDeskAssistant.bat" -ForegroundColor Yellow

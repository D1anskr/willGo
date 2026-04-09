param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Get-ProjectVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content $ProjectPath -Raw
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$versionNode)) {
        return '1.0.0'
    }

    return [string]$versionNode
}

function New-BatchFile {
    param(
        [string]$Path,
        [string]$ScriptRelativePath,
        [string]$SuccessMessage,
        [string]$FailureMessage,
        [string]$Arguments = ''
    )

    $invokeLine = if ([string]::IsNullOrWhiteSpace($Arguments)) {
        "powershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$ScriptRelativePath`""
    }
    else {
        "powershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$ScriptRelativePath`" $Arguments"
    }

    $content = @"
@echo off
setlocal
$invokeLine
if errorlevel 1 (
  echo.
  echo $FailureMessage Press any key to exit.
  pause >nul
  exit /b 1
)
echo.
echo $SuccessMessage Press any key to exit.
pause >nul
"@

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $content, $encoding)
}

function New-InstallerPackage {
    param(
        [string]$RepoRoot,
        [string]$PackageRoot,
        [string]$Version,
        [string]$Runtime,
        [string]$Configuration
    )

    $projectPath = Join-Path $RepoRoot 'src/FloatingDeskAssistant/FloatingDeskAssistant.csproj'
    $publishDir = Join-Path $PackageRoot 'app'
    $assetsDir = Join-Path $PackageRoot 'assets'
    $toolsDir = Join-Path $PackageRoot 'tools'
    $packageTemplateDir = Join-Path $RepoRoot 'scripts/windows/package'
    $brandingScript = Join-Path $RepoRoot 'scripts/windows/generate-branding-assets.ps1'
    $sourceAssetsDir = Join-Path $RepoRoot 'src/FloatingDeskAssistant/Assets'

    Write-Step 'Generating branding assets'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $brandingScript -OutputDir $sourceAssetsDir
    if ($LASTEXITCODE -ne 0) {
        throw 'Branding asset generation failed.'
    }

    if (Test-Path $PackageRoot) {
        Remove-Item -Path $PackageRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null

    Write-Step 'Publishing self-contained app'
    dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
    if ((-not $?) -or $LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    Write-Step 'Copying installer assets'
    Copy-Item -Path (Join-Path $packageTemplateDir 'install.ps1') -Destination (Join-Path $toolsDir 'install.ps1') -Force
    Copy-Item -Path (Join-Path $packageTemplateDir 'uninstall.ps1') -Destination (Join-Path $toolsDir 'uninstall.ps1') -Force
    Copy-Item -Path (Join-Path $packageTemplateDir 'README.txt') -Destination (Join-Path $PackageRoot 'README.txt') -Force
    Copy-Item -Path (Join-Path $sourceAssetsDir 'willGo.ico') -Destination (Join-Path $assetsDir 'willGo.ico') -Force
    Copy-Item -Path (Join-Path $sourceAssetsDir 'willGo.png') -Destination (Join-Path $assetsDir 'willGo.png') -Force

    $packageInfo = [ordered]@{
        appDisplayName = 'willGo'
        version = $Version
        publisher = 'willGo'
        installFolderName = 'willGo'
        executableName = 'FloatingDeskAssistant.exe'
        shortcutName = 'willGo'
        startMenuFolderName = 'willGo'
        iconFileName = 'willGo.ico'
        uninstallKeyName = 'willGo'
        userDataFolderName = 'FloatingDeskAssistant'
        processNames = @('FloatingDeskAssistant')
        runtime = $Runtime
        configuration = $Configuration
        builtAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    }
    $packageInfo | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $PackageRoot 'package-info.json') -Encoding UTF8

    New-BatchFile -Path (Join-Path $PackageRoot 'Install-willGo.bat') -ScriptRelativePath 'tools\install.ps1' -SuccessMessage 'Install completed.' -FailureMessage 'Install failed.'
    New-BatchFile -Path (Join-Path $PackageRoot 'Uninstall-willGo.bat') -ScriptRelativePath 'tools\uninstall.ps1' -SuccessMessage 'Uninstall completed.' -FailureMessage 'Uninstall failed.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$projectPath = Join-Path $repoRoot 'src/FloatingDeskAssistant/FloatingDeskAssistant.csproj'
$version = Get-ProjectVersion -ProjectPath $projectPath
$distRoot = Join-Path $repoRoot 'dist'
$packageRoot = Join-Path $distRoot "willGo-$version-$Runtime"
$zipPath = "$packageRoot.zip"

Write-Step 'Building installer package'
New-InstallerPackage -RepoRoot $repoRoot -PackageRoot $packageRoot -Version $version -Runtime $Runtime -Configuration $Configuration

if ($Zip) {
    Write-Step 'Creating zip archive'
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Archive created: $zipPath" -ForegroundColor Green
}

Write-Step 'Completed'
Write-Host "Package folder: $packageRoot" -ForegroundColor Green